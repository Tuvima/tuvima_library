import SwiftUI
import TuvimaCore

@MainActor
final class LibraryViewModel: ObservableObject {
    enum Screen {
        case server
        case loading(String)
        case pairing(PairingSession)
        case library(DisplayPage)
        case search(SearchResponse)
        case detail(DetailPage, UUID?)
        case error(String)
    }

    @Published var screen: Screen = .server
    private var api: TuvimaAPIClient?

    func start(environment: AppEnvironment) {
        guard let api = environment.api else { return }
        connect(api)
    }

    func connect(_ address: String, environment: AppEnvironment) {
        do { connect(try environment.connect(address)) }
        catch { screen = .error(error.localizedDescription) }
    }

    func openLane(_ lane: String) {
        guard let api else { return }
        screen = .loading("Loading \(lane)…")
        Task {
            do { screen = .library(try await api.browse(lane: lane)) }
            catch { screen = .error(error.localizedDescription) }
        }
    }

    func search(_ query: String) {
        guard let api, !query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        screen = .loading("Searching…")
        Task {
            do { screen = .search(try await api.search(query: query)) }
            catch { screen = .error(error.localizedDescription) }
        }
    }

    func openDetail(entityType: String, id: UUID, playableAssetID: UUID? = nil) {
        guard let api else { return }
        screen = .loading("Loading details…")
        Task {
            do { screen = .detail(try await api.details(entityType: entityType, id: id), playableAssetID) }
            catch { screen = .error(error.localizedDescription) }
        }
    }

    private func connect(_ api: TuvimaAPIClient) {
        self.api = api
        screen = .loading("Connecting…")
        Task {
            do {
                _ = try await api.discoverServer()
                if await api.isPaired { screen = .library(try await api.home()) }
                else {
                    let pairing = try await api.beginPairing(scopes: Self.scopes)
                    screen = .pairing(pairing)
                    await poll(pairing)
                }
            } catch { screen = .error(error.localizedDescription) }
        }
    }

    private func poll(_ pairing: PairingSession) async {
        guard let api else { return }
        var interval = pairing.interval
        let deadline = Date().addingTimeInterval(TimeInterval(pairing.expiresIn))
        while Date() < deadline {
            try? await Task.sleep(for: .seconds(interval))
            do {
                switch try await api.pollPairing(pairing) {
                case .authorized: screen = .library(try await api.home()); return
                case .pending(let next): interval = next
                case let .failed(_, description): screen = .error(description ?? "Pairing failed."); return
                }
            } catch { screen = .error(error.localizedDescription); return }
        }
        screen = .error("The pairing code expired.")
    }

    private static let scopes =
        "library.read artwork.read progress.read progress.write queue.read queue.write playback.read playback.write downloads.read downloads.write"
}

struct ContentView: View {
    @EnvironmentObject private var environment: AppEnvironment
    @EnvironmentObject private var playback: PlaybackCoordinator
    @StateObject private var model = LibraryViewModel()
    @State private var server = ""
    @State private var query = ""

    var body: some View {
        NavigationStack {
            Group {
                switch model.screen {
                case .server:
                    Form {
                        Section("Connect to Tuvima Library") {
                            TextField("https://library.example", text: $server)
                                .textInputAutocapitalization(.never)
                                .keyboardType(.URL)
                            Button("Connect") { model.connect(server, environment: environment) }
                        }
                    }
                case .loading(let message):
                    ProgressView(message)
                case .pairing(let pairing):
                    VStack(spacing: 20) {
                        Text("Pair this device").font(.largeTitle.bold())
                        Text(pairing.verificationURI)
                        Text(pairing.userCode).font(.system(size: 44, weight: .bold, design: .monospaced))
                        ProgressView("Waiting for approval…")
                    }.padding()
                case .library(let page):
                    library(page)
                case .search(let response):
                    searchResults(response)
                case let .detail(detail, playableAssetID):
                    detailView(detail, playableAssetID: playableAssetID)
                case .error(let message):
                    ContentUnavailableView(
                        "Tuvima needs attention",
                        systemImage: "exclamationmark.triangle",
                        description: Text(message)
                    ).overlay(alignment: .bottom) {
                        Button("Connect again") { model.screen = .server }.buttonStyle(.borderedProminent).padding()
                    }
                }
            }
            .navigationTitle("Tuvima Library")
        }
        .safeAreaInset(edge: .bottom) {
            if let title = playback.title {
                HStack {
                    Text(title).lineLimit(1)
                    Spacer()
                    Button(playback.isPlaying ? "Pause" : "Play") {
                        playback.isPlaying ? playback.pause() : playback.resume()
                    }
                }.padding().background(.ultraThinMaterial)
            }
        }
        .task {
            model.start(environment: environment)
            if let link = environment.consumePendingDetail() {
                model.openDetail(entityType: link.entityType, id: link.id)
            }
        }
        .onChange(of: environment.pendingDetail) { _, value in
            if let value {
                model.openDetail(entityType: value.entityType, id: value.id)
                _ = environment.consumePendingDetail()
            }
        }
    }

    @ViewBuilder
    private func library(_ page: DisplayPage) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                HStack {
                    ForEach(["watch", "read", "listen"], id: \.self) { lane in
                        Button(lane.capitalized) { model.openLane(lane) }.buttonStyle(.bordered)
                    }
                }.padding(.horizontal)
                HStack {
                    TextField("Search your library", text: $query)
                        .textFieldStyle(.roundedBorder)
                        .onSubmit { model.search(query) }
                    Button("Search") { model.search(query) }
                }.padding(.horizontal)
                ForEach(page.shelves) { shelf in
                    VStack(alignment: .leading) {
                        Text(shelf.title).font(.title2.bold()).padding(.horizontal)
                        ScrollView(.horizontal, showsIndicators: false) {
                            LazyHStack(spacing: 12) {
                                ForEach(shelf.items) { card in
                                    VStack(alignment: .leading) {
                                        Button {
                                            let target = appleCardDetailTarget(card)
                                            model.openDetail(
                                                entityType: target.entityType,
                                                id: target.id,
                                                playableAssetID: card.playableAssetID
                                            )
                                        } label: {
                                            RoundedRectangle(cornerRadius: 12)
                                                .fill(.purple.gradient)
                                                .frame(width: 150, height: 210)
                                                .overlay(Text(card.title).multilineTextAlignment(.center).padding())
                                        }.buttonStyle(.plain)
                                        HStack {
                                            if let asset = card.playableAssetID {
                                                Button("Play", systemImage: "play.fill") {
                                                    playback.play(assetID: asset, title: card.title)
                                                }.labelStyle(.iconOnly)
                                                Button("Download", systemImage: "arrow.down.circle") {
                                                    environment.downloads.download(assetID: asset)
                                                }.labelStyle(.iconOnly)
                                            }
                                        }
                                    }.frame(width: 150)
                                }
                            }.padding(.horizontal)
                        }
                    }
                }
            }.padding(.vertical)
        }
    }

    @ViewBuilder
    private func searchResults(_ response: SearchResponse) -> some View {
        List(response.sections) { section in
            Section(section.title) {
                ForEach(section.results) { result in
                    Button {
                        if let target = result.detailTarget {
                            model.openDetail(entityType: target.entityType, id: target.id)
                        }
                    } label: {
                        VStack(alignment: .leading) {
                            Text(result.title)
                            if let subtitle = result.subtitle { Text(subtitle).font(.caption).foregroundStyle(.secondary) }
                        }
                    }.disabled(result.detailTarget == nil)
                }
            }
        }.searchable(text: $query).onSubmit(of: .search) { model.search(query) }
    }

    @ViewBuilder
    private func detailView(_ detail: DetailPage, playableAssetID: UUID?) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text(detail.title).font(.largeTitle.bold())
                if let subtitle = detail.subtitle { Text(subtitle).font(.title3).foregroundStyle(.secondary) }
                if let summary = detail.facts?.summary, !summary.isEmpty { Text(summary).font(.headline) }
                if let description = detail.description { Text(description) }
                if let asset = playableAssetID {
                    HStack {
                        Button("Play", systemImage: "play.fill") { playback.play(assetID: asset, title: detail.title) }
                            .buttonStyle(.borderedProminent)
                        Button("Download", systemImage: "arrow.down.circle") { environment.downloads.download(assetID: asset) }
                            .buttonStyle(.bordered)
                    }
                }
                Button("Home") { model.start(environment: environment) }.buttonStyle(.bordered)
            }.padding()
        }
    }
}

private func appleDetailEntityType(_ mediaType: String) -> String {
    let value = mediaType.lowercased()
    if value.contains("movie") { return "movie" }
    if value.contains("episode") { return "tvEpisode" }
    if value.contains("tv") { return "tvShow" }
    if value.contains("audiobook") { return "audiobook" }
    if value.contains("book") { return "book" }
    if value.contains("comic") { return "comicIssue" }
    if value.contains("music") { return "musicAlbum" }
    return "work"
}

private func appleCardDetailTarget(_ card: DisplayCard) -> (entityType: String, id: UUID) {
    for action in card.actions {
        if let route = action.webUrl, route.contains("/details/") {
            let parts = route.split(separator: "/")
            if parts.count >= 2, let id = UUID(uuidString: String(parts.last!)) {
                return (String(parts[parts.count - 2]), id)
            }
        }
    }
    if let collectionID = card.collectionId { return ("collection", collectionID) }
    return (appleDetailEntityType(card.mediaType), card.workId ?? card.id)
}
