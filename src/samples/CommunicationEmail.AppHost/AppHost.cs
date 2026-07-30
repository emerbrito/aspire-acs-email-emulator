using Aspire.Hosting.ApplicationModel;
using EmBrito.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aca-env");

var usePublishedEmailEmulator = bool.TryParse(
    builder.Configuration["EmailEmulator:UsePublishedImage"],
    out var configuredUsePublishedEmailEmulator)
    && configuredUsePublishedEmailEmulator;

var email = builder
    .AddAzureCommunicationEmail("email")
    .RunAsEmulator(
        emulator =>
        {
            if (usePublishedEmailEmulator)
            {
                emulator.WithImagePullPolicy(ImagePullPolicy.Always);
                return;
            }

            emulator.WithDockerfile(
                "../..",
                "EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/Dockerfile");
        });

builder
    .AddProject<Projects.CommunicationEmail_Api>("api")
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health")
    .WithReference(email)
    .WaitFor(email);

builder.Build().Run();
