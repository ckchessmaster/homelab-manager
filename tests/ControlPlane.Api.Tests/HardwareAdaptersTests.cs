using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ControlPlane.Api.Features.Adapters.Redfish;
using ControlPlane.Api.Features.Adapters.UniFi;
using ControlPlane.Api.Storage;
using EFCore.NamingConventions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Api.Tests;

public class HardwareAdaptersTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public MockHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    [Fact]
    public async Task RedfishClient_GetSystemInfoAsync_ParsesPowerStateAndVitals()
    {
        var mockJson = """
        {
            "PowerState": "On",
            "Model": "PowerEdge R740xd",
            "BiosVersion": "2.16.0",
            "SerialNumber": "H8997J2",
            "Status": {
                "Health": "OK",
                "State": "Enabled"
            }
        }
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("/redfish/v1/Systems/System.Embedded.1", req.RequestUri!.ToString());
            Assert.NotNull(req.Headers.Authorization);
            Assert.Equal("Basic", req.Headers.Authorization.Scheme);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockJson, Encoding.UTF8, "application/json")
            };
        });

        var factory = new MockHttpClientFactory(handler);
        var client = new RedfishClient(factory, NullLogger<RedfishClient>.Instance);

        var info = await client.GetSystemInfoAsync("192.168.1.120", "root", "calvin");

        Assert.NotNull(info);
        Assert.Equal("On", info.PowerState);
        Assert.Equal("PowerEdge R740xd", info.Model);
        Assert.Equal("2.16.0", info.BiosVersion);
        Assert.Equal("OK", info.HealthStatus);
        Assert.Equal("H8997J2", info.SerialNumber);
    }

    [Fact]
    public async Task RedfishClient_GetThermalVitalsAsync_ParsesTemperaturesAndFans()
    {
        var mockJson = """
        {
            "Temperatures": [
                {
                    "Name": "CPU1 Temp",
                    "ReadingCelsius": 48.0,
                    "UpperThresholdCritical": 95.0,
                    "Status": { "Health": "OK" }
                },
                {
                    "Name": "Inlet Temp",
                    "ReadingCelsius": 22.0,
                    "UpperThresholdCritical": 45.0,
                    "Status": { "Health": "OK" }
                }
            ],
            "Fans": [
                {
                    "FanName": "System Board Fan1",
                    "Reading": 4500,
                    "Status": { "Health": "OK" }
                },
                {
                    "FanName": "System Board Fan2",
                    "Reading": 4620,
                    "Status": { "Health": "OK" }
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("/redfish/v1/Chassis/System.Embedded.1/Thermal", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockJson, Encoding.UTF8, "application/json")
            };
        });

        var factory = new MockHttpClientFactory(handler);
        var client = new RedfishClient(factory, NullLogger<RedfishClient>.Instance);

        var vitals = await client.GetThermalVitalsAsync("192.168.1.120", "root", "calvin");

        Assert.NotNull(vitals);
        Assert.Equal(2, vitals.Temperatures.Count);
        Assert.Equal("CPU1 Temp", vitals.Temperatures[0].Name);
        Assert.Equal(48.0, vitals.Temperatures[0].CurrentReadingCelsius);
        Assert.Equal(2, vitals.Fans.Count);
        Assert.Equal("System Board Fan1", vitals.Fans[0].Name);
        Assert.Equal(4500, vitals.Fans[0].ReadingRpm);
    }

    [Fact]
    public async Task RedfishClient_ResetSystemAsync_DispatchesResetType()
    {
        string? sentPayload = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains("/redfish/v1/Systems/System.Embedded.1/Actions/ComputerSystem.Reset", req.RequestUri!.ToString());
            sentPayload = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var factory = new MockHttpClientFactory(handler);
        var client = new RedfishClient(factory, NullLogger<RedfishClient>.Instance);

        var result = await client.ResetSystemAsync("192.168.1.120", "root", "calvin", "ForceRestart");

        Assert.True(result.Success);
        Assert.NotNull(sentPayload);
        Assert.Contains("ForceRestart", sentPayload);
    }

    [Fact]
    public async Task UniFiClient_CyclePoEPortAsync_CyclesPowerOffAndAuto()
    {
        var putRequests = new List<string>();

        var handler = new MockHttpMessageHandler(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (uri.Contains("/api/auth/login") || uri.Contains("/api/login"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
                };
            }

            if (uri.Contains("/stat/device"))
            {
                var deviceJson = """
                {
                    "data": [
                        {
                            "_id": "dev-12345",
                            "mac": "00:11:22:33:44:55",
                            "name": "USW-24-PoE",
                            "port_overrides": [
                                { "port_idx": 5, "poe_mode": "auto" }
                            ]
                        }
                    ]
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(deviceJson, Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Put && uri.Contains("/rest/device/dev-12345"))
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                putRequests.Add(body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = new MockHttpClientFactory(handler);
        var client = new UniFiClient(factory, NullLogger<UniFiClient>.Instance);

        // Run with 0s delay for fast testing
        var result = await client.CyclePoEPortAsync(
            controllerUrl: "https://192.168.1.1",
            username: "admin",
            password: "password123",
            switchMac: "00:11:22:33:44:55",
            portNumber: 5,
            delaySeconds: 0
        );

        Assert.True(result.Success);
        Assert.Equal(2, putRequests.Count);
        Assert.Contains("\"poe_mode\":\"off\"", putRequests[0]);
        Assert.Contains("\"poe_mode\":\"auto\"", putRequests[1]);
    }

    [Fact]
    public async Task UniFiClient_GetActiveClientsAsync_ParsesMacLeaseTable()
    {
        var mockJson = """
        {
            "data": [
                {
                    "mac": "aa:bb:cc:dd:ee:ff",
                    "ip": "192.168.1.150",
                    "hostname": "pi-k8s-node-01",
                    "last_seen": 1700000000
                },
                {
                    "mac": "11:22:33:44:55:66",
                    "ip": "192.168.1.151",
                    "hostname": "pi-k8s-node-02",
                    "last_seen": 1700000010
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("/api/auth/login"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
                };
            }

            if (req.RequestUri!.ToString().Contains("/stat/sta"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(mockJson, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = new MockHttpClientFactory(handler);
        var client = new UniFiClient(factory, NullLogger<UniFiClient>.Instance);

        var clients = await client.GetActiveClientsAsync("https://192.168.1.1", "admin", "password123");

        Assert.NotNull(clients);
        Assert.Equal(2, clients.Count);
        Assert.Equal("aa:bb:cc:dd:ee:ff", clients[0].Mac);
        Assert.Equal("192.168.1.150", clients[0].Ip);
        Assert.Equal("pi-k8s-node-01", clients[0].Hostname);
    }
}
