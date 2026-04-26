using Bunit;
using MediaEngine.Web.Components.Shared.Shell;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class SidebarShellRenderTests : TestContext
{
    public SidebarShellRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void SidebarPageShell_RendersRailHeaderAndContentSlots()
    {
        var cut = RenderComponent<SidebarPageShell>(parameters => parameters
            .Add(component => component.Rail, builder => builder.AddMarkupContent(0, "<nav id=\"rail-slot\">Rail</nav>"))
            .Add(component => component.Header, builder => builder.AddMarkupContent(1, "<header id=\"header-slot\">Header</header>"))
            .AddChildContent("<section id=\"content-slot\">Content</section>"));

        Assert.Single(cut.FindAll(".sidebar-page"));
        Assert.Single(cut.FindAll(".sidebar-rail"));
        Assert.Single(cut.FindAll(".sidebar-content"));
        Assert.NotNull(cut.Find("#rail-slot"));
        Assert.NotNull(cut.Find("#header-slot"));
        Assert.NotNull(cut.Find("#content-slot"));
    }

    [Fact]
    public void SidebarNavGroup_RendersExpandedChildrenAndBadge()
    {
        var cut = RenderComponent<SidebarNavGroup>(parameters => parameters
            .Add(component => component.Label, "Admin")
            .Add(component => component.Icon, MudBlazor.Icons.Material.Outlined.AdminPanelSettings)
            .Add(component => component.Expandable, true)
            .Add(component => component.Expanded, true)
            .Add(component => component.BadgeCount, 4)
            .AddChildContent("<span id=\"child-item\">Child</span>"));

        Assert.Contains("Admin", cut.Markup);
        Assert.Contains("4", cut.Markup);
        Assert.NotNull(cut.Find("#child-item"));
        Assert.Contains("aria-expanded", cut.Markup);
    }
}
