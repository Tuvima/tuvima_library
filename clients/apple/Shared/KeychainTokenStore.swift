import Foundation
import Security

public final class KeychainTokenStore: @unchecked Sendable {
    private let service = "com.tuvima.library.native.credentials.v1"

    public init() {}

    public func save(_ token: ClientToken, for origin: URL) throws {
        let data = try JSONEncoder().encode(token)
        let query = baseQuery(origin)
        SecItemDelete(query as CFDictionary)
        var insert = query
        insert[kSecValueData as String] = data
        insert[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        let status = SecItemAdd(insert as CFDictionary, nil)
        guard status == errSecSuccess else { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }
    }

    public func load(for origin: URL) -> ClientToken? {
        var query = baseQuery(origin)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var value: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &value) == errSecSuccess,
              let data = value as? Data else { return nil }
        return try? JSONDecoder().decode(ClientToken.self, from: data)
    }

    public func clear(for origin: URL) {
        SecItemDelete(baseQuery(origin) as CFDictionary)
    }

    private func baseQuery(_ origin: URL) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: origin.absoluteString,
        ]
    }
}
