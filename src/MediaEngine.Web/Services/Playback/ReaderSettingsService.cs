using Microsoft.JSInterop;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Playback;

/// <summary>
/// Manages EPUB reader visual preferences in browser localStorage.
/// Settings are per-device — not synced to the Engine.
/// </summary>
public sealed class ReaderSettingsService : IAsyncDisposable
{
    private const string StorageKey = "tuvima_reader_settings";
    private readonly IJSRuntime _js;
    private ReaderSettingsDto _current = new();
    private bool _loaded;

    public ReaderSettingsService(IJSRuntime js) => _js = js;

    /// <summary>Current reader settings (cached in memory after first load).</summary>
    public ReaderSettingsDto Current => _current;

    /// <summary>Load settings from localStorage. Safe to call multiple times (idempotent).</summary>
    public async Task LoadAsync()
    {
        if (_loaded) return;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(json))
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<ReaderSettingsDto>(json);
                if (parsed is not null) _current = parsed;
            }
        }
        catch
        {
            // localStorage unavailable (SSR pre-render) — use defaults
        }
        _loaded = true;
    }

    /// <summary>Save current settings to localStorage.</summary>
    public async Task SaveAsync()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_current);
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch
        {
            // localStorage unavailable — settings are in-memory only
        }
    }

    /// <summary>Update a setting and persist.</summary>
    public async Task UpdateAsync(Action<ReaderSettingsDto> configure)
    {
        configure(_current);
        await SaveAsync();
    }

    /// <summary>Apply current settings to the reader's CSS custom properties.</summary>
    public async Task ApplyToReaderAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("epubReader.setReaderStyle", "--reader-font-family", _current.FontFamily);
            await _js.InvokeVoidAsync("epubReader.setReaderStyle", "--reader-font-size", $"{_current.FontSize}px");
            await _js.InvokeVoidAsync("epubReader.setReaderStyle", "--reader-line-height", _current.LineHeight.ToString("F1"));
            await _js.InvokeVoidAsync("epubReader.setReaderStyle", "--reader-margin", $"{_current.Margins}px");
        }
        catch
        {
            // JS interop not available
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
