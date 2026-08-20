using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class LibraryAccessEvaluatorTests
{
    private readonly LibraryAccessEvaluator _evaluator = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _member = Guid.NewGuid();

    [Theory]
    [InlineData(LibraryAccessAction.Read)]
    [InlineData(LibraryAccessAction.Contribute)]
    [InlineData(LibraryAccessAction.Manage)]
    public void Owner_HasAllLibraryAccess(LibraryAccessAction action)
    {
        Assert.True(_evaluator.IsAllowed(
            new LibraryAccessSubject(_owner, AppRoles.Consumer),
            Policy(LibraryVisibility.Private),
            action));
    }

    [Fact]
    public void PrivateLibrary_DoesNotExposeContentToAnotherProfile()
    {
        var subject = new LibraryAccessSubject(_member, AppRoles.Consumer);
        var policy = Policy(LibraryVisibility.Private);

        Assert.False(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Read));
        Assert.False(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Contribute));
        Assert.False(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Manage));
    }

    [Fact]
    public void SharedMember_CanReadAndContribute_ButCannotManage()
    {
        var subject = new LibraryAccessSubject(_member, AppRoles.Consumer);
        var policy = Policy(LibraryVisibility.Shared, new HashSet<Guid> { _member });

        Assert.True(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Read));
        Assert.True(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Contribute));
        Assert.False(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Manage));
    }

    [Fact]
    public void HouseholdMember_CanRead_ButCannotUploadWithoutExplicitGrant()
    {
        var subject = new LibraryAccessSubject(_member, AppRoles.Consumer);
        var policy = Policy(LibraryVisibility.Household);

        Assert.True(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Read));
        Assert.False(_evaluator.IsAllowed(subject, policy, LibraryAccessAction.Contribute));
    }

    [Fact]
    public void AdministratorAccess_ObeysTheLibraryPolicy()
    {
        var administrator = new LibraryAccessSubject(_member, AppRoles.Administrator);

        Assert.True(_evaluator.IsAllowed(
            administrator,
            Policy(LibraryVisibility.Private) with { AllowAdministratorAccess = true },
            LibraryAccessAction.Manage));
        Assert.False(_evaluator.IsAllowed(
            administrator,
            Policy(LibraryVisibility.Private) with { AllowAdministratorAccess = false },
            LibraryAccessAction.Read));
    }

    private LibraryAccessPolicy Policy(string visibility, IReadOnlySet<Guid>? authorized = null) => new()
    {
        OwnerProfileId = _owner,
        Visibility = visibility,
        AuthorizedProfileIds = authorized ?? new HashSet<Guid>(),
    };
}
