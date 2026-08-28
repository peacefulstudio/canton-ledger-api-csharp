// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public class PackageNameResolutionTests
{
    private const string HoldingPackageName = "splice-api-token-holding-v1";

    [Fact]
    public void ForPackageName_prefixes_the_package_name_with_the_by_package_name_marker()
    {
        var identifier = Identifier.ForPackageName(
            HoldingPackageName, "Splice.Api.Token.HoldingV1", "Holding");

        identifier.PackageId.Should().Be($"#{HoldingPackageName}");
        identifier.ModuleName.Should().Be("Splice.Api.Token.HoldingV1");
        identifier.EntityName.Should().Be("Holding");
    }

    [Fact]
    public void ForPackageName_rejects_a_package_name_that_already_carries_the_prefix()
    {
        var building = () => Identifier.ForPackageName($"#{HoldingPackageName}", "Module", "Holding");

        building.Should().Throw<ArgumentException>()
            .WithMessage($"*'#{HoldingPackageName}' already carries*")
            .And.ParamName.Should().Be("packageName");
    }

    [Theory]
    [InlineData("", "Module", "Holding")]
    [InlineData("   ", "Module", "Holding")]
    [InlineData("pkg", "", "Holding")]
    [InlineData("pkg", "Module", "  ")]
    public void ForPackageName_rejects_a_blank_component(
        string packageName, string moduleName, string entityName)
    {
        var building = () => Identifier.ForPackageName(packageName, moduleName, entityName);

        building.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ResolvePackageIdAsync_returns_the_package_id_of_the_only_matching_package()
    {
        var resolver = ResolverOver(Package("00holding", HoldingPackageName, "1.0.0"));

        var packageId = await resolver.ResolvePackageIdAsync(
            HoldingPackageName, TestContext.Current.CancellationToken);

        packageId.Should().Be("00holding");
    }

    [Fact]
    public async Task ResolvePackageIdAsync_compares_versions_component_wise_rather_than_as_text()
    {
        var resolver = ResolverOver(
            Package("00nine", HoldingPackageName, "2.9.0"),
            Package("00ten", HoldingPackageName, "2.10.0"));

        var packageId = await resolver.ResolvePackageIdAsync(
            HoldingPackageName, TestContext.Current.CancellationToken);

        packageId.Should().Be("00ten", "2.10.0 outranks 2.9.0 numerically even though it sorts lower as text");
    }

    [Fact]
    public async Task ResolvePackageIdAsync_ignores_packages_carrying_a_different_name()
    {
        var resolver = ResolverOver(
            Package("00other", "splice-api-token-metadata-v1", "9.9.9"),
            Package("00holding", HoldingPackageName, "1.0.0"));

        var packageId = await resolver.ResolvePackageIdAsync(
            HoldingPackageName, TestContext.Current.CancellationToken);

        packageId.Should().Be("00holding");
    }

    [Fact]
    public async Task ResolvePackageIdAsync_lists_the_participant_packages_once_per_package_name()
    {
        var adminClient = AdminClientKnowing(Package("00holding", HoldingPackageName, "1.0.0"));
        var resolver = new PackageIdResolver(adminClient);

        await resolver.ResolvePackageIdAsync(HoldingPackageName, TestContext.Current.CancellationToken);
        await resolver.ResolvePackageIdAsync(HoldingPackageName, TestContext.Current.CancellationToken);

        await adminClient.Received(1).ListKnownPackagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvePackageIdAsync_throws_naming_the_package_name_the_participant_does_not_know()
    {
        var resolver = ResolverOver(Package("00other", "splice-api-token-metadata-v1", "1.0.0"));

        var resolving = async () => await resolver.ResolvePackageIdAsync(
            HoldingPackageName, TestContext.Current.CancellationToken);

        (await resolving.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"The participant knows no package named '{HoldingPackageName}'.");
    }

    [Fact]
    public async Task ResolvePackageIdAsync_throws_naming_a_matching_package_whose_version_is_not_numeric()
    {
        var resolver = ResolverOver(Package("00holding", HoldingPackageName, "1.0.0-rc1"));

        var resolving = async () => await resolver.ResolvePackageIdAsync(
            HoldingPackageName, TestContext.Current.CancellationToken);

        (await resolving.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*(00holding)*'1.0.0-rc1'*");
    }

    [Fact]
    public void Constructor_rejects_a_null_admin_client() =>
        ((Action)(() => _ = new PackageIdResolver(null!))).Should().Throw<ArgumentNullException>();

    private static PackageDetails Package(string packageId, string name, string version) =>
        new(packageId, name, version, PackageSize: 1024, KnownSince: DateTimeOffset.UnixEpoch);

    private static IAdminClient AdminClientKnowing(params PackageDetails[] packages)
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.ListKnownPackagesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PackageDetails>>(packages));
        return adminClient;
    }

    private static PackageIdResolver ResolverOver(params PackageDetails[] packages) =>
        new(AdminClientKnowing(packages));
}
