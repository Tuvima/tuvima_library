import SwiftUI

@main
struct TuvimaApp: App {
    @StateObject private var environment = AppEnvironment.shared

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(environment)
                .environmentObject(environment.playback)
                .onOpenURL(perform: environment.handleDeepLink)
        }
    }
}
