using Aspire.Hosting.Yarp.Transforms;

var builder = DistributedApplication.CreateBuilder(args);

var apiKey = builder.AddParameter("api-key", secret: true);
var masterKey = builder.AddParameter("master-key", secret: true);
var enableMcpServer = builder.AddParameter("enable-mcp-server", "true");
var zitadelMasterKey = builder.AddParameter("zitadel-masterkey", "MasterkeyNeedsToHave32Characters", secret: true);
var zitadelClientId = builder.AddParameter("zitadel-client-id", "389775242525999110");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var controlPlaneDb = postgres.AddDatabase("ControlPlaneDatabase", "controlplane");
var temporalDb = postgres.AddDatabase("TemporalDatabase", "temporal");
var zitadelDb = postgres.AddDatabase("ZitadelDatabase", "zitadel");

var temporal = builder.AddContainer("temporal", "temporalio/temporal")
    .WithArgs("server", "start-dev", "--ip", "0.0.0.0", "--port", "7233", "--ui-port", "8233")
    .WithHttpEndpoint(port: 7233, targetPort: 7233, name: "grpc")
    .WithHttpEndpoint(port: 8233, targetPort: 8233, name: "ui");

var bootstrapDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../../docker/compose/zitadel/bootstrap"));
Directory.CreateDirectory(bootstrapDir);

var zitadelApi = builder.AddContainer("zitadel-api", "ghcr.io/zitadel/zitadel")
    .WithArgs("start-from-init", "--masterkeyFromEnv", "--tlsMode", "disabled")
    .WithEnvironment("ZITADEL_MASTERKEY", zitadelMasterKey)
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_DATABASE", "zitadel")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_USERNAME", "postgres")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_PASSWORD", postgres.Resource.PasswordParameter)
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_SSL_MODE", "disable")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_USERNAME", "postgres")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_PASSWORD", postgres.Resource.PasswordParameter)
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_SSL_MODE", "disable")
    .WithEnvironment("ZITADEL_EXTERNALDOMAIN", "localhost")
    .WithEnvironment("ZITADEL_EXTERNALPORT", "8085")
    .WithEnvironment("ZITADEL_EXTERNALSECURE", "false")
    .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORDCHANGEREQUIRED", "false")
    .WithEnvironment("ZITADEL_FIRSTINSTANCE_LOGINCLIENTPATPATH", "/zitadel/bootstrap/login-client.pat")
    .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_MACHINE_USERNAME", "login-client")
    .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_MACHINE_NAME", "Automatically Initialized IAM_LOGIN_CLIENT")
    .WithEnvironment("ZITADEL_FIRSTINSTANCE_ORG_LOGINCLIENT_PAT_EXPIRATIONDATE", "2035-01-01T00:00:00Z")
    .WithEnvironment("ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED", "true")
    .WithEnvironment("ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_BASEURI", "http://localhost:8085/ui/v2/login/")
    .WithEnvironment("ZITADEL_OIDC_DEFAULTLOGINURLV2", "http://localhost:8085/ui/v2/login/login?authRequest=")
    .WithEnvironment("ZITADEL_OIDC_DEFAULTLOGOUTURLV2", "http://localhost:8085/ui/v2/login/logout?post_logout_redirect=")
    .WithBindMount(bootstrapDir, "/zitadel/bootstrap")
    .WithHttpEndpoint(targetPort: 8080, name: "http")
    .WithHttpHealthCheck("/debug/healthz")
    .WithReference(zitadelDb)
    .WaitFor(zitadelDb);

var zitadelLogin = builder.AddContainer("zitadel-login", "ghcr.io/zitadel/zitadel-login")
    .WithReference(zitadelApi.GetEndpoint("http"))
    .WithEnvironment("ZITADEL_API_URL", zitadelApi.GetEndpoint("http"))
    .WithEnvironment("NEXT_PUBLIC_BASE_PATH", "/ui/v2/login")
    .WithEnvironment("ZITADEL_SERVICE_USER_TOKEN_FILE", "/zitadel/bootstrap/login-client.pat")
    .WithEnvironment("CUSTOM_REQUEST_HEADERS", "x-zitadel-instance-host:localhost:8085,x-zitadel-public-host:localhost:8085")
    .WithBindMount(bootstrapDir, "/zitadel/bootstrap", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 3000, name: "http")
    .WaitFor(zitadelApi);

var zitadel = builder.AddYarp("zitadel")
    .WithHttpEndpoint(port: 8085, targetPort: 8085, name: "http")
    .WithConfiguration(yarp =>
    {
        var loginCluster = yarp.AddCluster(zitadelLogin.GetEndpoint("http"));
        var apiCluster = yarp.AddCluster(zitadelApi.GetEndpoint("http"));

        yarp.AddRoute("/ui/v2/login", loginCluster)
            .WithTransformUseOriginalHostHeader(true)
            .WithTransformRequestHeader("x-zitadel-instance-host", "localhost:8085", append: false)
            .WithTransformRequestHeader("x-zitadel-public-host", "localhost:8085", append: false);
        yarp.AddRoute("/ui/v2/login/{**catch-all}", loginCluster)
            .WithTransformUseOriginalHostHeader(true)
            .WithTransformRequestHeader("x-zitadel-instance-host", "localhost:8085", append: false)
            .WithTransformRequestHeader("x-zitadel-public-host", "localhost:8085", append: false);
        yarp.AddRoute("/{**catch-all}", apiCluster)
            .WithTransformUseOriginalHostHeader(true)
            .WithTransformRequestHeader("x-zitadel-instance-host", "localhost:8085", append: false)
            .WithTransformRequestHeader("x-zitadel-public-host", "localhost:8085", append: false);
    });

var api = builder.AddProject<Projects.ControlPlane_Api>("api")
    .WithReference(controlPlaneDb)
    .WaitFor(controlPlaneDb)
    .WithReference(temporal.GetEndpoint("grpc"))
    .WaitFor(temporal)
    .WaitFor(zitadel)
    .WithHttpEndpoint(port: 5029, targetPort: 5029, isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:5029")
    .WithEnvironment("ControlPlane__ApiKey", apiKey)
    .WithEnvironment("CONTROLPLANE_MASTER_KEY", masterKey)
    .WithEnvironment("Temporal__ServerUrl", temporal.GetEndpoint("grpc"))
    .WithEnvironment("ENABLE_MCP_SERVER", enableMcpServer)
    .WithEnvironment("Zitadel__Authority", zitadel.GetEndpoint("http"))
    .WithEnvironment("Zitadel__Audience", "controlplane")
    .WithEnvironment("Zitadel__ValidAudiences__0", zitadelClientId)
    .WithEnvironment("Zitadel__RequireHttpsMetadata", "false");

builder.AddViteApp("frontend", "../../frontend")
    .WithNpm(install: false)
    .WithHttpEndpoint(port: 5173, targetPort: 5173, isProxied: false)
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("VITE_ZITADEL_AUTHORITY", zitadel.GetEndpoint("http"))
    .WithEnvironment("VITE_ZITADEL_CLIENT_ID", zitadelClientId)
    .WithExternalHttpEndpoints();

builder.Build().Run();
