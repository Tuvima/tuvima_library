import Foundation
import Network

public actor TuvimaAPIClient {
    public let origin: URL
    private let clientID: String
    private let clientName: String
    private let clientVersion: String
    private let deviceName: String
    private let capabilities: [String: Any]
    private let store: KeychainTokenStore
    private let session: URLSession
    private var discovery: Discovery?
    private var token: ClientToken?

    public init(
        serverAddress: String,
        clientID: String = "tuvima-ios",
        clientName: String = "Tuvima for iOS",
        clientVersion: String,
        deviceName: String,
        store: KeychainTokenStore = KeychainTokenStore()
    ) throws {
        guard var components = URLComponents(string: serverAddress.contains("://") ? serverAddress : "https://\(serverAddress)"),
              let host = components.host else { throw TuvimaClientError.invalidServer }
        components.path = ""
        components.query = nil
        components.fragment = nil
        guard let origin = components.url else { throw TuvimaClientError.invalidServer }
        if origin.scheme != "https" && !Self.isLocalHost(host) { throw TuvimaClientError.insecureRemoteServer }
        self.origin = origin
        self.clientID = clientID
        self.clientName = clientName
        self.clientVersion = clientVersion
        self.deviceName = deviceName
        self.store = store
        self.token = store.load(for: origin)
        self.capabilities = [
            "schema_version": 1,
            "containers": ["mp4", "m4a", "mp3", "mov", "mpegts"],
            "video_codecs": ["h264", "hevc"],
            "audio_codecs": ["aac", "mp3", "alac", "flac", "ac3", "eac3"],
            "subtitle_formats": ["webvtt", "vtt"],
            "protocols": ["https", "http-range", "hls"],
            "max_width": 3840,
            "max_height": 2160,
            "max_audio_channels": 8,
            "supports_hdr": true,
            "supports_playback_speed": true,
            "supports_offline_downloads": true,
        ]
        let configuration = URLSessionConfiguration.default
        configuration.waitsForConnectivity = true
        configuration.timeoutIntervalForRequest = 30
        self.session = URLSession(configuration: configuration)
    }

    public var isPaired: Bool { token != nil }

    public func discoverServer() async throws -> Discovery {
        let value: Discovery = try await get("/.well-known/tuvima", authenticated: false)
        guard value.supportedAPIVersions.contains("1") else { throw TuvimaClientError.unsupportedAPI }
        discovery = value
        return value
    }

    public func beginPairing(scopes: String) async throws -> PairingSession {
        let endpoints = try await endpoints()
        return try await post(endpoints.deviceAuthorizationEndpoint, json: [
            "client_id": clientID,
            "client_name": clientName,
            "client_version": clientVersion,
            "device_name": deviceName,
            "device_class": "mobile",
            "scope": scopes,
            "capabilities": capabilities,
        ], authenticated: false)
    }

    public func pollPairing(_ pairing: PairingSession) async throws -> PairingPoll {
        let endpoints = try await endpoints()
        do {
            let response: TokenResponse = try await post(endpoints.tokenEndpoint, json: [
                "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
                "client_id": clientID,
                "device_code": pairing.deviceCode,
            ], authenticated: false)
            return .authorized(try accept(response))
        } catch TuvimaClientError.http(_, let body) {
            let error = try? JSONDecoder().decode(OAuthError.self, from: Data(body.utf8))
            if error?.error == "authorization_pending" || error?.error == "slow_down" {
                return .pending(interval: error?.interval ?? pairing.interval)
            }
            return .failed(code: error?.error ?? "request_failed", description: error?.errorDescription)
        }
    }

    public func home() async throws -> DisplayPage { try await get("/api/v1/display/home") }

    public func continuePage(lane: String) async throws -> DisplayPage {
        try await get("/api/v1/display/continue?lane=\(lane)")
    }

    public func browse(lane: String, mediaType: String? = nil) async throws -> DisplayPage {
        var components = URLComponents()
        components.queryItems = [URLQueryItem(name: "lane", value: lane)]
        if let mediaType { components.queryItems?.append(URLQueryItem(name: "mediaType", value: mediaType)) }
        return try await get("/api/v1/display/browse?\(components.percentEncodedQuery ?? "")")
    }

    public func search(query: String) async throws -> SearchResponse {
        var components = URLComponents()
        components.queryItems = [URLQueryItem(name: "q", value: query)]
        return try await get("/api/v1/display/search?\(components.percentEncodedQuery ?? "")")
    }

    public func details(entityType: String, id: UUID) async throws -> DetailPage {
        try await get("/api/v1/details/\(entityType)/\(id.uuidString)")
    }

    public func playbackManifest(assetID: UUID, connectionPath: String = "local") async throws -> PlaybackManifest {
        try await get("/api/v1/playback/\(assetID.uuidString)/manifest?connectionPath=\(connectionPath)")
    }

    public func requestOfflineVariant(assetID: UUID) async throws {
        let _: EncodeJobResponse = try await post(
            "/api/v1/playback/\(assetID.uuidString)/encode",
            json: ["profileKey": "mobile-standard"]
        )
    }

    public func playerQueue() async throws -> [PlayerQueueItem] {
        let state: PlayerState = try await get("/api/v1/player/state")
        return state.queue.filter {
            $0.mediaType.caseInsensitiveCompare("Music") == .orderedSame ||
                $0.mediaType.caseInsensitiveCompare("Audiobooks") == .orderedSame
        }
    }

    public func authorizedRequest(path: String) async throws -> URLRequest {
        var request = URLRequest(url: try resolve(path))
        request.setValue("Bearer \(try await accessToken())", forHTTPHeaderField: "Authorization")
        return request
    }

    public func absoluteURL(path: String) throws -> URL { try resolve(path) }

    public func heartbeat(assetID: UUID, isPlaying: Bool, position: Double, duration: Double?, rate: Double) async throws {
        var body: [String: Any] = [
            "assetId": assetID.uuidString,
            "isPlaying": isPlaying,
            "positionSeconds": position,
            "playbackRate": rate,
        ]
        if let duration { body["durationSeconds"] = duration }
        let _: IgnoredResponse = try await post("/api/v1/player/heartbeat", json: body)
    }

    public func signOut() {
        token = nil
        store.clear(for: origin)
    }

    private func accessToken(forceRefresh: Bool = false) async throws -> String {
        guard let current = token else { throw TuvimaClientError.notPaired }
        if !forceRefresh && !current.expiresSoon { return current.accessToken }
        let endpoints = try await endpoints()
        let response: TokenResponse = try await post(endpoints.tokenEndpoint, json: [
            "grant_type": "refresh_token",
            "client_id": clientID,
            "refresh_token": current.refreshToken,
        ], authenticated: false)
        return try accept(response).accessToken
    }

    private func accept(_ response: TokenResponse) throws -> ClientToken {
        let value = ClientToken(
            accessToken: response.accessToken,
            refreshToken: response.refreshToken,
            expiresAt: Date().addingTimeInterval(response.expiresIn),
            scope: response.scope,
            deviceID: response.deviceID,
            profileID: response.profileID
        )
        token = value
        try store.save(value, for: origin)
        return value
    }

    private func endpoints() async throws -> Discovery {
        if let discovery { return discovery }
        return try await discoverServer()
    }

    private func get<T: Decodable>(_ path: String, authenticated: Bool = true) async throws -> T {
        var request = URLRequest(url: try resolve(path))
        request.httpMethod = "GET"
        return try await send(request, authenticated: authenticated)
    }

    private func post<T: Decodable>(_ path: String, json: [String: Any], authenticated: Bool = true) async throws -> T {
        var request = URLRequest(url: try resolve(path))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONSerialization.data(withJSONObject: json)
        return try await send(request, authenticated: authenticated)
    }

    private func send<T: Decodable>(_ original: URLRequest, authenticated: Bool) async throws -> T {
        var request = original
        if authenticated { request.setValue("Bearer \(try await accessToken())", forHTTPHeaderField: "Authorization") }
        var (data, response) = try await session.data(for: request)
        if authenticated, (response as? HTTPURLResponse)?.statusCode == 401 {
            request.setValue("Bearer \(try await accessToken(forceRefresh: true))", forHTTPHeaderField: "Authorization")
            (data, response) = try await session.data(for: request)
        }
        guard let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode) else {
            throw TuvimaClientError.http((response as? HTTPURLResponse)?.statusCode ?? 0, String(decoding: data, as: UTF8.self))
        }
        let decoder = JSONDecoder()
        return try decoder.decode(T.self, from: data)
    }

    private func resolve(_ path: String) throws -> URL {
        if let absolute = URL(string: path), absolute.scheme != nil { return absolute }
        guard let resolved = URL(string: path, relativeTo: origin)?.absoluteURL else { throw TuvimaClientError.invalidServer }
        return resolved
    }

    private static func isLocalHost(_ host: String) -> Bool {
        if host == "localhost" || host.hasSuffix(".local") || host.hasPrefix("10.") || host.hasPrefix("192.168.") {
            return true
        }
        let octets = host.split(separator: ".").compactMap { Int($0) }
        return octets.count == 4 && octets[0] == 172 && (16...31).contains(octets[1])
    }
}

private struct EncodeJobResponse: Decodable { let id: UUID }
private struct IgnoredResponse: Decodable {}
