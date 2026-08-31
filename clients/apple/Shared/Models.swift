import Foundation

public struct Discovery: Decodable, Sendable {
    public let serverName: String
    public let apiBaseURL: String
    public let supportedAPIVersions: [String]
    public let deviceAuthorizationEndpoint: String
    public let tokenEndpoint: String
    public let verificationURI: String
    public let capabilities: [String]

    enum CodingKeys: String, CodingKey {
        case serverName = "server_name"
        case apiBaseURL = "api_base_url"
        case supportedAPIVersions = "supported_api_versions"
        case deviceAuthorizationEndpoint = "device_authorization_endpoint"
        case tokenEndpoint = "token_endpoint"
        case verificationURI = "verification_uri"
        case capabilities
    }
}

public struct PairingSession: Decodable, Sendable {
    public let deviceCode: String
    public let userCode: String
    public let verificationURI: String
    public let verificationURIComplete: String
    public let expiresIn: Int
    public let interval: Int

    enum CodingKeys: String, CodingKey {
        case deviceCode = "device_code"
        case userCode = "user_code"
        case verificationURI = "verification_uri"
        case verificationURIComplete = "verification_uri_complete"
        case expiresIn = "expires_in"
        case interval
    }
}

public struct ClientToken: Codable, Sendable {
    public let accessToken: String
    public let refreshToken: String
    public let expiresAt: Date
    public let scope: String
    public let deviceID: UUID
    public let profileID: UUID

    public var expiresSoon: Bool { expiresAt.timeIntervalSinceNow < 30 }

    enum CodingKeys: String, CodingKey {
        case accessToken = "access_token"
        case refreshToken = "refresh_token"
        case expiresAt = "expires_at"
        case scope
        case deviceID = "device_id"
        case profileID = "profile_id"
    }
}

struct TokenResponse: Decodable {
    let accessToken: String
    let refreshToken: String
    let expiresIn: TimeInterval
    let scope: String
    let deviceID: UUID
    let profileID: UUID

    enum CodingKeys: String, CodingKey {
        case accessToken = "access_token"
        case refreshToken = "refresh_token"
        case expiresIn = "expires_in"
        case scope
        case deviceID = "device_id"
        case profileID = "profile_id"
    }
}

public struct DisplayPage: Decodable, Sendable {
    public let key: String
    public let title: String
    public let subtitle: String?
    public let shelves: [DisplayShelf]
}

public struct DisplayShelf: Decodable, Identifiable, Sendable {
    public var id: String { key }
    public let key: String
    public let title: String
    public let subtitle: String?
    public let items: [DisplayCard]
}

public struct DisplayCard: Decodable, Identifiable, Sendable {
    public let id: UUID
    public let workId: UUID?
    public let assetId: UUID?
    public let collectionId: UUID?
    public let mediaType: String
    public let title: String
    public let subtitle: String?
    public let facts: [String]
    public let actions: [DisplayAction]

    public var playableAssetID: UUID? { actions.compactMap(\.assetId).first ?? assetId }
}

public struct DisplayAction: Decodable, Sendable {
    public let type: String
    public let label: String
    public let workId: UUID?
    public let assetId: UUID?
    public let collectionId: UUID?
    public let webUrl: String?
}

public struct SearchResponse: Decodable, Sendable {
    public let query: String
    public let sections: [SearchSection]
}

public struct SearchSection: Decodable, Identifiable, Sendable {
    public var id: String { key }
    public let key: String
    public let title: String
    public let results: [SearchResult]
}

public struct SearchResult: Decodable, Identifiable, Sendable {
    public let id: UUID
    public let entityType: String
    public let mediaType: String?
    public let title: String
    public let subtitle: String?
    public let description: String?
    public let detailRoute: String

    public var detailTarget: (entityType: String, id: UUID)? {
        let parts = detailRoute.split(separator: "/")
        guard parts.count >= 2, let id = UUID(uuidString: String(parts[parts.count - 1])) else { return nil }
        return (String(parts[parts.count - 2]), id)
    }
}

public struct DetailPage: Decodable, Identifiable, Sendable {
    public let id: String
    public let entityType: String
    public let title: String
    public let subtitle: String?
    public let description: String?
    public let facts: DetailFacts?
}

public struct DetailFacts: Decodable, Sendable {
    public let year: String?
    public let rating: String?
    public let contentRating: String?
    public let runtime: String?
    public let duration: String?

    public var summary: String {
        [year, rating, contentRating, runtime, duration]
            .compactMap { $0?.isEmpty == false ? $0 : nil }
            .reduce(into: [String]()) { if !$0.contains($1) { $0.append($1) } }
            .joined(separator: " · ")
    }
}

public struct PlaybackManifest: Decodable, Sendable {
    public let assetId: UUID
    public let recommendedDelivery: String
    public let directPlaySupported: Bool
    public let directStreamUrl: String?
    public let hlsUrl: String?
    public let hlsStatus: String?
    public let hlsExpiresAt: String?
    public let resume: PlaybackResume?
    public let offlineVariants: [OfflineVariant]
    public let warnings: [String]
}

public struct PlaybackResume: Decodable, Sendable {
    public let positionSeconds: Double
    public let durationSeconds: Double?
    public let completed: Bool
}

public struct OfflineVariant: Decodable, Sendable {
    public let id: UUID
    public let assetId: UUID
    public let status: String
    public let downloadUrl: String?
    public let fileSizeBytes: Int64?
}

public struct PlayerQueueItem: Decodable, Sendable {
    public let assetId: UUID?
    public let mediaType: String
    public let title: String
    public let subtitle: String?
}

struct PlayerState: Decodable {
    let queue: [PlayerQueueItem]
}

public enum PairingPoll: Sendable {
    case authorized(ClientToken)
    case pending(interval: Int)
    case failed(code: String, description: String?)
}

struct OAuthError: Decodable {
    let error: String
    let errorDescription: String?
    let interval: Int?

    enum CodingKeys: String, CodingKey {
        case error
        case errorDescription = "error_description"
        case interval
    }
}

public enum TuvimaClientError: LocalizedError {
    case invalidServer
    case insecureRemoteServer
    case unsupportedAPI
    case notPaired
    case http(Int, String)
    case noPlayableDelivery

    public var errorDescription: String? {
        switch self {
        case .invalidServer: "Enter a valid Tuvima Dashboard address."
        case .insecureRemoteServer: "Remote Tuvima connections require HTTPS."
        case .unsupportedAPI: "This server does not advertise API v1."
        case .notPaired: "This device has not been paired."
        case let .http(status, _): "Tuvima returned HTTP \(status)."
        case .noPlayableDelivery: "The server did not provide a playable delivery."
        }
    }
}
