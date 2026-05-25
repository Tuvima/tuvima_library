using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace MediaEngine.Web.Components.Shared;

public sealed class AppCssElement : ComponentBase
{
    [Parameter] public string Tag { get; set; } = "div";
    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Css { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }
    [Parameter] public EventCallback<MouseEventArgs> OnDoubleClick { get; set; }
    [Parameter] public EventCallback<MouseEventArgs> OnContextMenu { get; set; }
    [Parameter] public EventCallback<DragEventArgs> OnDragStart { get; set; }
    [Parameter] public EventCallback<DragEventArgs> OnDragEnd { get; set; }
    [Parameter] public EventCallback<DragEventArgs> OnDragOver { get; set; }
    [Parameter] public EventCallback<DragEventArgs> OnDrop { get; set; }
    [Parameter] public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }
    [Parameter] public bool StopClickPropagation { get; set; }
    [Parameter] public bool PreventContextMenuDefault { get; set; }
    [Parameter] public bool PreventDragOverDefault { get; set; }
    [Parameter] public bool PreventDropDefault { get; set; }
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
    public ElementReference Element { get; private set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, string.IsNullOrWhiteSpace(Tag) ? "div" : Tag);
        if (!string.IsNullOrWhiteSpace(Class))
        {
            builder.AddAttribute(1, "class", Class);
        }

        if (!string.IsNullOrWhiteSpace(Css))
        {
            builder.AddAttribute(2, "style", Css);
        }

        if (AdditionalAttributes is not null)
        {
            builder.AddMultipleAttributes(3, AdditionalAttributes);
        }

        if (OnClick.HasDelegate)
        {
            builder.AddAttribute(4, "onclick", OnClick);
        }

        if (StopClickPropagation)
        {
            builder.AddEventStopPropagationAttribute(5, "onclick", true);
        }

        if (OnDoubleClick.HasDelegate)
        {
            builder.AddAttribute(6, "ondblclick", OnDoubleClick);
        }

        if (OnContextMenu.HasDelegate)
        {
            builder.AddAttribute(7, "oncontextmenu", OnContextMenu);
        }

        if (PreventContextMenuDefault)
        {
            builder.AddEventPreventDefaultAttribute(8, "oncontextmenu", true);
        }

        if (OnDragStart.HasDelegate)
        {
            builder.AddAttribute(9, "ondragstart", OnDragStart);
        }

        if (OnDragEnd.HasDelegate)
        {
            builder.AddAttribute(10, "ondragend", OnDragEnd);
        }

        if (OnDragOver.HasDelegate)
        {
            builder.AddAttribute(11, "ondragover", OnDragOver);
        }

        if (PreventDragOverDefault)
        {
            builder.AddEventPreventDefaultAttribute(12, "ondragover", true);
        }

        if (OnDrop.HasDelegate)
        {
            builder.AddAttribute(13, "ondrop", OnDrop);
        }

        if (PreventDropDefault)
        {
            builder.AddEventPreventDefaultAttribute(14, "ondrop", true);
        }

        if (OnKeyDown.HasDelegate)
        {
            builder.AddAttribute(15, "onkeydown", OnKeyDown);
        }

        builder.AddElementReferenceCapture(16, element => Element = element);
        builder.AddContent(17, ChildContent);
        builder.CloseElement();
    }
}
