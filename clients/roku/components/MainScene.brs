sub init()
    m.serverGroup = m.top.findNode("serverGroup")
    m.pairingGroup = m.top.findNode("pairingGroup")
    m.libraryGroup = m.top.findNode("libraryGroup")
    m.searchGroup = m.top.findNode("searchGroup")
    m.keyboard = m.top.findNode("serverKeyboard")
    m.rows = m.top.findNode("libraryRows")
    m.video = m.top.findNode("video")
    m.status = m.top.findNode("status")
    m.api = m.top.findNode("api")
    m.pairingTimer = m.top.findNode("pairingTimer")
    m.heartbeatTimer = m.top.findNode("heartbeatTimer")
    m.registry = CreateObject("roRegistrySection", "tuvima.native.v1")
    m.pendingOperation = ""
    m.api.observeField("result", "onApiResult")
    m.api.observeField("error", "onApiError")
    m.api.observeField("tokenUpdate", "onTokenUpdate")
    m.top.findNode("connectButton").observeField("buttonSelected", "onConnect")
    m.top.findNode("homeButton").observeField("buttonSelected", "loadHome")
    m.top.findNode("watchButton").observeField("buttonSelected", "loadWatch")
    m.top.findNode("readButton").observeField("buttonSelected", "loadRead")
    m.top.findNode("listenButton").observeField("buttonSelected", "loadListen")
    m.top.findNode("searchButton").observeField("buttonSelected", "showSearch")
    m.top.findNode("submitSearchButton").observeField("buttonSelected", "submitSearch")
    m.rows.observeField("rowItemFocused", "onItemFocused")
    m.rows.observeField("rowItemSelected", "onItemSelected")
    m.video.observeField("state", "onVideoState")
    m.pairingTimer.observeField("fire", "pollPairing")
    m.heartbeatTimer.observeField("fire", "sendHeartbeat")

    server = m.registry.Read("server")
    token = m.registry.Read("access_token")
    refreshToken = m.registry.Read("refresh_token")
    if server = ""
        showServer()
    else
        m.api.serverOrigin = server
        m.api.accessToken = token
        m.api.refreshToken = refreshToken
        if token = "" then beginPairing() else loadHome()
    end if
end sub

sub showServer()
    m.serverGroup.visible = true
    m.pairingGroup.visible = false
    m.libraryGroup.visible = false
    m.keyboard.setFocus(true)
end sub

sub onConnect()
    server = m.keyboard.text
    while Right(server, 1) = "/": server = Left(server, Len(server) - 1): end while
    if Left(server, 7) <> "http://" and Left(server, 8) <> "https://"
        m.status.text = "Enter a complete HTTP or HTTPS Dashboard address."
        return
    end if
    m.registry.Write("server", server)
    m.registry.Flush()
    m.api.serverOrigin = server
    runApi("discover", {})
end sub

sub beginPairing()
    payload = {
        client_id: "tuvima-roku",
        client_name: "Tuvima for Roku",
        client_version: "0.1.0",
        device_name: "Roku",
        device_class: "television",
        scope: "library.read artwork.read progress.read progress.write queue.read queue.write playback.read playback.write",
        capabilities: {
            schema_version: 1,
            containers: ["mp4", "mpegts"],
            video_codecs: ["h264", "hevc", "vp9"],
            audio_codecs: ["aac", "ac3", "eac3"],
            subtitle_formats: ["webvtt", "vtt"],
            protocols: ["https", "http-range", "hls"],
            max_width: 3840,
            max_height: 2160,
            max_audio_channels: 8,
            supports_hdr: true,
            supports_playback_speed: false,
            supports_offline_downloads: false
        }
    }
    runApi("pair", payload)
end sub

sub pollPairing()
    if m.retryAssetId <> invalid
        assetId = m.retryAssetId
        m.retryAssetId = invalid
        m.pairingTimer.control = "stop"
        runApi("manifest", { assetId: assetId })
        return
    end if
    if m.deviceCode = invalid then return
    runApi("token", {
        grant_type: "urn:ietf:params:oauth:grant-type:device_code",
        client_id: "tuvima-roku",
        device_code: m.deviceCode
    })
end sub

sub loadHome(): runApi("home", {}): end sub
sub loadWatch(): runApi("browse", { lane: "watch" }): end sub
sub loadRead(): runApi("browse", { lane: "read" }): end sub
sub loadListen(): runApi("browse", { lane: "listen" }): end sub

sub showSearch()
    m.libraryGroup.visible = false
    m.searchGroup.visible = true
    m.top.findNode("searchKeyboard").setFocus(true)
end sub

sub submitSearch()
    query = m.top.findNode("searchKeyboard").text
    if query <> "" then runApi("search", { query: query })
end sub

sub runApi(operation as String, payload as Object)
    if m.api.state = "RUN" then return
    m.pendingOperation = operation
    m.api.operation = operation
    m.api.payload = payload
    m.api.control = "RUN"
end sub

sub onApiResult()
    result = m.api.result
    if result = invalid then return
    operation = result.operation
    value = result.value
    if operation = "discover"
        supported = false
        for each version in value.supported_api_versions: if version = "1" then supported = true
        end for
        if not supported then m.status.text = "This server does not support API v1.": return
        beginPairing()
    else if operation = "pair"
        m.deviceCode = value.device_code
        m.top.findNode("verificationUri").text = value.verification_uri
        m.top.findNode("userCode").text = value.user_code
        m.pairingTimer.duration = value.interval
        m.serverGroup.visible = false
        m.pairingGroup.visible = true
        m.pairingTimer.control = "start"
    else if operation = "token"
        m.pairingTimer.control = "stop"
        m.api.accessToken = value.access_token
        m.api.refreshToken = value.refresh_token
        m.registry.Write("access_token", value.access_token)
        m.registry.Write("refresh_token", value.refresh_token)
        m.registry.Flush()
        loadHome()
    else if operation = "home" or operation = "browse"
        showLibrary(value)
    else if operation = "search"
        showSearchResults(value)
    else if operation = "details"
        showDetails(value)
    else if operation = "manifest"
        playManifest(value)
    end if
end sub

sub onTokenUpdate()
    tokens = m.api.tokenUpdate
    if tokens = invalid then return
    m.registry.Write("access_token", tokens.accessToken)
    m.registry.Write("refresh_token", tokens.refreshToken)
    m.registry.Flush()
end sub

sub onApiError()
    error = m.api.error
    if error = invalid then return
    if error.operation = "token" and (error.code = "authorization_pending" or error.code = "slow_down")
        if error.interval <> invalid then m.pairingTimer.duration = error.interval
        return
    end if
    if error.status = 401
        m.registry.Delete("access_token")
        m.registry.Delete("refresh_token")
        m.registry.Flush()
    end if
    m.status.text = error.description
end sub

sub showLibrary(page as Object)
    m.serverGroup.visible = false
    m.pairingGroup.visible = false
    m.libraryGroup.visible = true
    m.searchGroup.visible = false
    root = CreateObject("roSGNode", "ContentNode")
    for each shelf in page.shelves
        row = root.CreateChild("ContentNode")
        row.title = shelf.title
        for each card in shelf.items
            item = row.CreateChild("ContentNode")
            item.title = card.title
            item.description = card.description
            artwork = card.artwork
            if artwork <> invalid
                if artwork.coverLargeUrl <> invalid then item.HDPosterUrl = absoluteMediaUrl(artwork.coverLargeUrl)
                if item.HDPosterUrl = "" and artwork.squareLargeUrl <> invalid then item.HDPosterUrl = absoluteMediaUrl(artwork.squareLargeUrl)
                if item.HDPosterUrl = "" and artwork.backgroundLargeUrl <> invalid then item.HDPosterUrl = absoluteMediaUrl(artwork.backgroundLargeUrl)
            end if
            assetId = ""
            if card.assetId <> invalid then assetId = card.assetId
            for each action in card.actions
                if action.assetId <> invalid then assetId = action.assetId: exit for
            end for
            item.AddFields({ assetId: assetId, entityId: card.id, entityType: detailEntityType(card.mediaType), mediaType: card.mediaType, httpHeaders: { Authorization: "Bearer " + m.api.accessToken } })
        end for
    end for
    m.rows.content = root
    m.rows.setFocus(true)
end sub

sub onItemFocused()
    location = m.rows.rowItemFocused
    if location = invalid or m.rows.content = invalid then return
    item = m.rows.content.getChild(location[0]).getChild(location[1])
    m.top.findNode("detailTitle").text = item.title
    m.top.findNode("detailText").text = item.description
end sub

sub onItemSelected()
    location = m.rows.rowItemSelected
    item = m.rows.content.getChild(location[0]).getChild(location[1])
    if item.assetId <> ""
        runApi("manifest", { assetId: item.assetId })
    else if item.entityId <> ""
        runApi("details", { entityType: item.entityType, id: item.entityId })
    end if
end sub

sub showSearchResults(response as Object)
    page = { shelves: [] }
    for each section in response.sections
        shelf = { title: section.title, items: [] }
        for each result in section.results
            shelf.items.Push({
                id: result.id,
                assetId: invalid,
                mediaType: result.mediaType,
                title: result.title,
                description: result.description,
                artwork: { coverLargeUrl: result.artworkUrl },
                actions: []
            })
        end for
        page.shelves.Push(shelf)
    end for
    showLibrary(page)
end sub

sub showDetails(detail as Object)
    m.top.findNode("detailTitle").text = detail.title
    m.top.findNode("detailText").text = detail.description
    if detail.primaryActions <> invalid
        for each action in detail.primaryActions
            if action.assetId <> invalid
                runApi("manifest", { assetId: action.assetId })
                return
            end if
        end for
    end if
end sub

sub playManifest(manifest as Object)
    path = invalid
    format = "mp4"
    if manifest.recommendedDelivery = "hls" and manifest.hlsStatus = "ready"
        path = manifest.hlsUrl
        format = "hls"
    else if manifest.recommendedDelivery = "direct-stream"
        path = manifest.directStreamUrl
    else if manifest.hlsStatus = "preparing"
        m.status.text = "Preparing adaptive playback…"
        m.retryAssetId = manifest.assetId
        m.pairingTimer.duration = 2
        m.pairingTimer.control = "start"
        return
    end if
    if path = invalid then m.status.text = "No compatible playback delivery is available.": return
    content = CreateObject("roSGNode", "ContentNode")
    content.url = absoluteMediaUrl(path)
    content.streamFormat = format
    content.HttpHeaders = { Authorization: "Bearer " + m.api.accessToken }
    m.video.content = content
    m.video.visible = true
    m.video.setFocus(true)
    if manifest.resume <> invalid then m.video.seek = manifest.resume.positionSeconds
    m.activeAssetId = manifest.assetId
    m.video.control = "play"
    m.heartbeatTimer.control = "start"
end sub

sub onVideoState()
    if m.video.state = "finished" or m.video.state = "error"
        m.heartbeatTimer.control = "stop"
        m.video.visible = false
        m.libraryGroup.visible = true
        m.rows.setFocus(true)
    end if
end sub

sub sendHeartbeat()
    if m.activeAssetId = invalid then return
    runApi("heartbeat", {
        assetId: m.activeAssetId,
        isPlaying: m.video.state = "playing",
        positionSeconds: m.video.position,
        durationSeconds: m.video.duration
    })
end sub

sub onLaunchContent()
    if m.top.launchContentId <> "" and m.api.accessToken <> ""
        runApi("manifest", { assetId: m.top.launchContentId })
    end if
end sub

function absoluteMediaUrl(path as String) as String
    if Left(path, 7) = "http://" or Left(path, 8) = "https://" then return path
    if Left(path, 1) <> "/" then path = "/" + path
    return m.api.serverOrigin + path
end function

function detailEntityType(mediaType as Dynamic) as String
    if mediaType = invalid then return "work"
    value = LCase(mediaType)
    if Instr(1, value, "movie") > 0 then return "movie"
    if Instr(1, value, "episode") > 0 then return "tvEpisode"
    if Instr(1, value, "tv") > 0 then return "tvShow"
    if Instr(1, value, "audiobook") > 0 then return "audiobook"
    if Instr(1, value, "book") > 0 then return "book"
    if Instr(1, value, "comic") > 0 then return "comicIssue"
    if Instr(1, value, "music") > 0 then return "musicAlbum"
    return "work"
end function

function onKeyEvent(key as String, press as Boolean) as Boolean
    if not press then return false
    if key = "back" and m.video.visible
        m.video.control = "stop"
        m.video.visible = false
        m.rows.setFocus(true)
        return true
    end if
    return false
end function
