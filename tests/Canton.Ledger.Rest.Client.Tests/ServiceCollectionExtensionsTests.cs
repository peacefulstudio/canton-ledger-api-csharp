// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.CodeDom.Compiler;
using System.Net.Http;
using System.Reflection;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

#pragma warning disable CANTONREST001

namespace Canton.Ledger.Rest.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildRawProvider(Action<IServiceCollection>? customize = null)
    {
        var services = new ServiceCollection();
        services.AddRestLedgerRawApis(options => options.HttpAddress = "http://ledger.example:7575");
        customize?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddRestLedgerClient_resolves_ILedgerReader_as_the_RestLedgerClient_adapter()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options => options.HttpAddress = "http://ledger.example:7575");
        using var provider = services.BuildServiceProvider();

        provider.GetService<ILedgerReader>().Should().BeOfType<RestLedgerClient>();
        provider.GetRequiredService<ITokenProvider>().Should().BeSameAs(ITokenProvider.None);

        using var httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServiceCollectionExtensions.HttpClientName);
        httpClient.BaseAddress.Should().Be(new Uri("http://ledger.example:7575"));
    }

    [Fact]
    public void AddRestLedgerClient_resolves_ILedgerStreamer_and_ILedgerClient_as_the_RestLedgerClient_adapter()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options => options.HttpAddress = "http://ledger.example:7575");
        using var provider = services.BuildServiceProvider();

        provider.GetService<ILedgerStreamer>().Should().BeOfType<RestLedgerClient>();
        provider.GetService<ILedgerClient>().Should().BeOfType<RestLedgerClient>();
    }

    [Fact]
    public void AddRestLedgerClient_resolves_ICantonLedgerClient_as_the_RestLedgerClient_adapter()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options => options.HttpAddress = "http://ledger.example:7575");
        using var provider = services.BuildServiceProvider();

        provider.GetService<Canton.Ledger.Abstractions.ICantonLedgerClient>().Should().BeOfType<RestLedgerClient>();
    }

    [Fact]
    public void AddRestLedgerRawApis_registers_the_raw_surface_but_not_the_ILedgerReader_adapter()
    {
        using var provider = BuildRawProvider();

        provider.GetService<IStateServiceApi>().Should().NotBeNull();
        provider.GetService<ILedgerReader>().Should().BeNull();
        provider.GetService<RestLedgerClient>().Should().BeNull();
    }

    public static TheoryData<Type> RefitterGeneratedInterfaces() =>
        [.. typeof(IVersionServiceApi).Assembly
            .GetTypes()
            .Where(type => type.IsInterface
                && type.IsPublic
                && type.GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "Refitter")];

    [Theory]
    [MemberData(nameof(RefitterGeneratedInterfaces))]
    public void AddRestLedgerRawApis_registers_every_Refitter_generated_service_interface(Type apiInterface)
    {
        using var provider = BuildRawProvider();

        provider.GetService(apiInterface).Should().NotBeNull(
            $"the generated interface {apiInterface.Name} must be registered; " +
            "a regen probably added a service — add it to ServiceCollectionExtensions");
    }

    [Fact]
    public void AddRestLedgerRawApis_registers_the_hand_authored_off_spec_interfaces()
    {
        using var provider = BuildRawProvider();

        provider.GetService<IAuthenticatedUserApi>().Should().NotBeNull();
        provider.GetService<IDarApi>().Should().NotBeNull();
        provider.GetService<IHealthApi>().Should().NotBeNull();
        provider.GetService<IInteractiveSubmissionApi>().Should().NotBeNull();
        provider.GetService<IPackageApi>().Should().NotBeNull();
    }

    [Fact]
    public void AddRestLedgerRawApis_binds_options_from_configuration()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HttpAddress"] = "http://ledger.example:7575"
            })
            .Build();

        services.AddRestLedgerRawApis(config);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<RestLedgerClientOptions>>()
            .Value.HttpAddress.Should().Be("http://ledger.example:7575");
    }

    [Fact]
    public void AddRestLedgerRawApis_fails_validation_when_HttpAddress_is_missing()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerRawApis(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task AddRestLedgerRawApis_wires_the_bearer_token_and_base_address_into_resolved_apis()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(System.Net.HttpStatusCode.OK, """{"version":"3.5.9"}""");
        var services = new ServiceCollection();
        services.AddCantonStaticAuth("static-token");
        services.AddRestLedgerRawApis(options => options.HttpAddress = "http://ledger.example:7575");
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
    public async Task AddRestLedgerRawApis_sends_no_Authorization_header_when_no_token_provider_is_registered()
    {
        var transport = new RecordingHttpHandler()
            .WithResponse(System.Net.HttpStatusCode.OK, """{"version":"3.5.9"}""");
        using var provider = BuildRawProvider(services =>
            services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => transport));

        var api = provider.GetRequiredService<IVersionServiceApi>();
        await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        transport.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task AddRestLedgerRawApis_called_twice_does_not_stack_duplicate_message_handlers()
    {
        const string isolatedHttpAddress = "http://dedup-pipeline-test.ledger.example:7575";

        var ownRequestActivities = new List<System.Diagnostics.Activity>();
        bool CapturesOwnRequest(System.Diagnostics.Activity activity) =>
            activity.GetTagItem(RestActivityHandler.UrlFull) is string url
            && url.StartsWith(isolatedHttpAddress, System.StringComparison.Ordinal);

        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == RestLedgerClient.ActivitySourceName,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (CapturesOwnRequest(activity)) ownRequestActivities.Add(activity);
            },
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var transport = new RecordingHttpHandler()
            .WithResponse(System.Net.HttpStatusCode.OK, """{"version":"3.5.9"}""");
        var services = new ServiceCollection();
        services.AddRestLedgerRawApis(options => options.HttpAddress = isolatedHttpAddress);
        services.AddRestLedgerRawApis(options => options.HttpAddress = isolatedHttpAddress);
        services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();

        var api = provider.GetRequiredService<IVersionServiceApi>();
        await api.GetLedgerApiVersion(TestContext.Current.CancellationToken);

        ownRequestActivities.Should().ContainSingle("registering twice must not stack a second handler pipeline");
    }

    [Fact]
    public void AddRestLedgerOptions_first_caller_wins_when_both_entry_points_are_combined_with_different_addresses()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerRawApis(options => options.HttpAddress = "http://first.example:7575");
        services.AddRestLedgerClient(options => options.HttpAddress = "http://second.example:9999");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RestLedgerClientOptions>>()
            .Value.HttpAddress.Should().Be("http://first.example:7575");
    }

    [Fact]
    public async Task AddRestLedgerRawApis_rewrites_ListUsers_paging_query_to_camelCase_end_to_end()
    {
        var transport = new RecordingHttpHandler().WithResponse(System.Net.HttpStatusCode.OK, """{"users":[]}""");
        using var provider = BuildRawProvider(services =>
            services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => transport));

        var api = provider.GetRequiredService<IUserManagementServiceApi>();
        await api.ListUsers(
            pageToken: null!,
            pageSize: 1,
            identityProviderId: null!,
            TestContext.Current.CancellationToken);

        transport.LastRequest!.RequestUri!.Query.Should().Be("?pageSize=1");
    }

    [Fact]
    public void AddRestLedgerRawApis_with_auth_configuration_registers_a_token_provider()
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

        services.AddRestLedgerRawApis(ledgerConfig, authConfig);

        using var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();
        tokenProvider.Should().NotBeSameAs(ITokenProvider.None);
    }

    [Fact]
    public void AddRestLedgerClient_fails_at_startup_when_Retry_MaxRetryAttempts_negative()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = "http://localhost:7575";
            options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = -1 };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MaxRetryAttempts*");
    }

    [Fact]
    public void AddRestLedgerClient_fails_at_startup_when_Retry_Delay_negative()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = "http://localhost:7575";
            options.Retry = new RetryOptions { Enabled = true, Delay = TimeSpan.FromMilliseconds(-1) };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*Delay*");
    }

    [Fact]
    public void AddRestLedgerClient_starts_when_Retry_configured_with_nonnegative_values()
    {
        var services = new ServiceCollection();
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = "http://localhost:7575";
            options.Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 5, Delay = TimeSpan.FromMilliseconds(200) };
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<RestLedgerClientOptions>>().Value;

        act.Should().NotThrow();
    }
}
