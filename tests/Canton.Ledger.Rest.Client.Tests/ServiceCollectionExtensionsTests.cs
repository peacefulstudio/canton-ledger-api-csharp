// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.CodeDom.Compiler;
using System.Reflection;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? customize = null)
    {
        var services = new ServiceCollection();
        services.AddRestLedgerApis(options => options.HttpAddress = "http://ledger.example:7575");
        customize?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public static TheoryData<Type> RefitterGeneratedInterfaces() =>
        [.. typeof(IVersionServiceApi).Assembly
            .GetTypes()
            .Where(type => type.IsInterface
                && type.IsPublic
                && type.GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "Refitter")];

    [Theory]
    [MemberData(nameof(RefitterGeneratedInterfaces))]
    public void AddRestLedgerApis_registers_every_Refitter_generated_service_interface(Type apiInterface)
    {
        using var provider = BuildProvider();

        provider.GetService(apiInterface).Should().NotBeNull(
            $"the generated interface {apiInterface.Name} must be registered; " +
            "a regen probably added a service — add it to ServiceCollectionExtensions");
    }

    [Fact]
    public void AddRestLedgerApis_registers_the_off_spec_trio_interfaces()
    {
        using var provider = BuildProvider();

        provider.GetService<IAuthenticatedUserApi>().Should().NotBeNull();
        provider.GetService<IHealthApi>().Should().NotBeNull();
    }

    [Fact]
    public void AddRestLedgerApis_binds_options_from_configuration()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HttpAddress"] = "http://ledger.example:7575"
            })
            .Build();

        services.AddRestLedgerApis(config);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<RestLedgerClientOptions>>()
            .Value.HttpAddress.Should().Be("http://ledger.example:7575");
    }

    [Fact]
    public void AddRestLedgerApis_fails_validation_when_HttpAddress_is_missing()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerApis(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task AddRestLedgerApis_wires_the_bearer_token_and_base_address_into_resolved_apis()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(System.Net.HttpStatusCode.OK, """{"version":"3.4.11"}""");
        var services = new ServiceCollection();
        services.AddCantonStaticAuth("static-token");
        services.AddRestLedgerApis(options => options.HttpAddress = "http://ledger.example:7575");
        services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var api = provider.GetRequiredService<IVersionServiceApi>();
        await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.ToString().Should().Be("http://ledger.example:7575/v2/version");
        transport.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        transport.LastRequest.Headers.Authorization.Parameter.Should().Be("static-token");
    }

    [Fact]
    public async Task AddRestLedgerApis_sends_no_Authorization_header_when_no_token_provider_is_registered()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(System.Net.HttpStatusCode.OK, """{"version":"3.4.11"}""");
        using var provider = BuildProvider(services =>
            services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => transport));

        var api = provider.GetRequiredService<IVersionServiceApi>();
        await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        transport.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task AddRestLedgerApis_called_twice_does_not_stack_duplicate_message_handlers()
    {
        const string isolatedHttpAddress = "http://dedup-pipeline-test.ledger.example:7575";

        var ownRequestActivities = new List<System.Diagnostics.Activity>();
        bool CapturesOwnRequest(System.Diagnostics.Activity activity) =>
            activity.GetTagItem(RestActivityHandler.UrlFull) is string url
            && url.StartsWith(isolatedHttpAddress, System.StringComparison.Ordinal);

        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == Kernel.Telemetry.LedgerActivitySource.NameFor<RestActivityHandler>(),
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (CapturesOwnRequest(activity)) ownRequestActivities.Add(activity);
            },
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var transport = new RecordingHttpHandler()
            .WithResponse(System.Net.HttpStatusCode.OK, """{"version":"3.4.11"}""");
        var services = new ServiceCollection();
        services.AddRestLedgerApis(options => options.HttpAddress = isolatedHttpAddress);
        services.AddRestLedgerApis(options => options.HttpAddress = isolatedHttpAddress);
        services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var api = provider.GetRequiredService<IVersionServiceApi>();
        await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        ownRequestActivities.Should().ContainSingle("registering twice must not stack a second handler pipeline");
    }

    [Fact]
    public void AddRestLedgerApis_with_auth_configuration_registers_a_token_provider()
    {
        var services = new ServiceCollection();
        var ledgerConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HttpAddress"] = "http://ledger.example:7575"
            })
            .Build();
        var authConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TokenEndpoint"] = "https://auth.example/token",
                ["ClientId"] = "client",
                ["ClientSecret"] = "secret"
            })
            .Build();

        services.AddRestLedgerApis(ledgerConfig, authConfig);

        using var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();
        tokenProvider.Should().NotBeSameAs(ITokenProvider.None);
    }
}
