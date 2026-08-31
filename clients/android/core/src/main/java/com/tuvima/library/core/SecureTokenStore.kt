package com.tuvima.library.core

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class SecureTokenStore(context: Context) {
    private val preferences = context.getSharedPreferences("tuvima_native_credentials", Context.MODE_PRIVATE)
    private val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    fun save(serverOrigin: String, token: ClientToken) {
        val payload = JSONObject()
            .put("access_token", token.accessToken)
            .put("refresh_token", token.refreshToken)
            .put("expires_at", token.expiresAtEpochSeconds)
            .put("scope", token.scope)
            .put("device_id", token.deviceId)
            .put("profile_id", token.profileId)
            .toString()
            .toByteArray(Charsets.UTF_8)
        val cipher = Cipher.getInstance(TRANSFORMATION).apply { init(Cipher.ENCRYPT_MODE, key()) }
        val encrypted = cipher.doFinal(payload)
        preferences.edit()
            .putString("server_origin", serverOrigin)
            .putString("token_iv", Base64.encodeToString(cipher.iv, Base64.NO_WRAP))
            .putString("token_payload", Base64.encodeToString(encrypted, Base64.NO_WRAP))
            .apply()
    }

    fun load(): Pair<String, ClientToken>? = runCatching {
        val origin = preferences.getString("server_origin", null) ?: return null
        val iv = Base64.decode(preferences.getString("token_iv", null), Base64.NO_WRAP)
        val payload = Base64.decode(preferences.getString("token_payload", null), Base64.NO_WRAP)
        val cipher = Cipher.getInstance(TRANSFORMATION).apply {
            init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, iv))
        }
        val json = JSONObject(String(cipher.doFinal(payload), Charsets.UTF_8))
        origin to ClientToken(
            accessToken = json.getString("access_token"),
            refreshToken = json.getString("refresh_token"),
            expiresAtEpochSeconds = json.getLong("expires_at"),
            scope = json.getString("scope"),
            deviceId = json.getString("device_id"),
            profileId = json.getString("profile_id"),
        )
    }.getOrNull()

    fun clear() {
        preferences.edit().clear().apply()
    }

    private fun key(): SecretKey {
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").run {
            init(
                KeyGenParameterSpec.Builder(
                    KEY_ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
                )
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .build(),
            )
            generateKey()
        }
    }

    private companion object {
        const val KEY_ALIAS = "tuvima.native.client.credentials.v1"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
    }
}
