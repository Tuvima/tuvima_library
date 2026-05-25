using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace MediaEngine.Web.Components.Shared;

public sealed class AppProgressFill : ComponentBase
{
    [Parameter] public double? Value { get; set; }
    [Parameter] public string Tag { get; set; } = "span";
    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Background { get; set; }
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private double ClampedValue => Math.Clamp(Value ?? 0, 0, 100);

    private string Css =>
        string.IsNullOrWhiteSpace(Background)
            ? $"width: {ClampedValue:F0}%;"
            : $"width: {ClampedValue:F0}%; background: {Background};";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, string.IsNullOrWhiteSpace(Tag) ? "span" : Tag);
        if (!string.IsNullOrWhiteSpace(Class))
        {
            builder.AddAttribute(1, "class", Class);
        }

        builder.AddAttribute(2, "style", Css);

        if (AdditionalAttributes is not null)
        {
            builder.AddMultipleAttributes(3, AdditionalAttributes);
        }

        builder.CloseElement();
    }
}
