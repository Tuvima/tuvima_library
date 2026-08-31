sub init()
    m.top.functionName = "execute"
end sub

sub execute()
    m.top.error = invalid
    m.top.result = invalid
    payload = m.top.payload
    if payload = invalid then payload = {}

    if m.top.operation = "discover"
        response = request("GET", "/.well-known/tuvima", invalid, false)
    else if m.top.operation = "pair"
        response = request("POST", "/api/v1/oauth/device_authorization", payload, false)
    else if m.top.operation = "token"
        response = request("POST", "/api/v1/oauth/token", payload, false)
    else if m.top.operation = "home"
        response = request("GET", "/api/v1/display/home", invalid, true)
    else if m.top.operation = "browse"
        response = request("GET", "/api/v1/display/browse?lane=" + encode(payload.lane), invalid, true)
    else if m.top.operation = "search"
        response = request("GET", "/api/v1/display/search?q=" + encode(payload.query), invalid, true)
    else if m.top.operation = "details"
        response = request("GET", "/api/v1/details/" + encode(payload.entityType) + "/" + encode(payload.id), invalid, true)
    else if m.top.operation = "manifest"
        response = request("GET", "/api/v1/playback/" + encode(payload.assetId) + "/manifest?connectionPath=local", invalid, true)
    else if m.top.operation = "heartbeat"
        response = request("POST", "/api/v1/player/heartbeat", payload, true)
    else
        response = { ok: false, status: 0, body: "Unknown API task operation." }
    end if

    parsed = invalid
    if response.body <> invalid and response.body <> "" then parsed = ParseJson(response.body)
    if response.ok
        if parsed = invalid then parsed = {}
        m.top.result = { operation: m.top.operation, value: parsed }
    else
        code = "request_failed"
        description = "Tuvima returned HTTP " + response.status.ToStr()
        interval = invalid
        if parsed <> invalid
            if parsed.error <> invalid then code = parsed.error
            if parsed.error_description <> invalid then description = parsed.error_description
            if parsed.interval <> invalid then interval = parsed.interval
        end if
        m.top.error = { operation: m.top.operation, status: response.status, code: code, description: description, interval: interval }
    end if
end sub

function request(method as String, path as String, body as Dynamic, authenticated as Boolean) as Object
    response = performRequest(method, path, body, authenticated)
    if authenticated and response.status = 401 and m.top.refreshToken <> ""
        refresh = performRequest("POST", "/api/v1/oauth/token", {
            grant_type: "refresh_token",
            client_id: "tuvima-roku",
            refresh_token: m.top.refreshToken
        }, false)
        if refresh.ok
            tokens = ParseJson(refresh.body)
            if tokens <> invalid
                m.top.accessToken = tokens.access_token
                m.top.refreshToken = tokens.refresh_token
                m.top.tokenUpdate = { accessToken: tokens.access_token, refreshToken: tokens.refresh_token }
                response = performRequest(method, path, body, true)
            end if
        end if
    end if
    return response
end function

function performRequest(method as String, path as String, body as Dynamic, authenticated as Boolean) as Object
    transfer = CreateObject("roUrlTransfer")
    transfer.SetCertificatesFile("common:/certs/ca-bundle.crt")
    transfer.InitClientCertificates()
    transfer.SetUrl(absoluteUrl(path))
    transfer.AddHeader("Accept", "application/json")
    if authenticated and m.top.accessToken <> ""
        transfer.AddHeader("Authorization", "Bearer " + m.top.accessToken)
    end if
    if method = "POST"
        transfer.AddHeader("Content-Type", "application/json")
        text = transfer.PostFromString(FormatJson(body))
    else
        text = transfer.GetToString()
    end if
    status = transfer.GetResponseCode()
    return { ok: status >= 200 and status < 300, status: status, body: text }
end function

function absoluteUrl(path as String) as String
    if Left(path, 7) = "http://" or Left(path, 8) = "https://" then return path
    if Left(path, 1) <> "/" then path = "/" + path
    return m.top.serverOrigin + path
end function

function encode(value as String) as String
    transfer = CreateObject("roUrlTransfer")
    return transfer.Escape(value)
end function
