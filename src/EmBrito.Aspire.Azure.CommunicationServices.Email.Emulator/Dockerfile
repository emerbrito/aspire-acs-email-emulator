FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator.csproj EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/
RUN dotnet restore EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator.csproj

COPY EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/ EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/
RUN dotnet publish EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator/EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir /data && chown -R app:app /data
USER app

ENV ASPNETCORE_HTTP_PORTS=8080
ENV Emulator__DatabasePath=/data/email.db
EXPOSE 8080

COPY --from=build --chown=app:app /app/publish .
ENTRYPOINT ["dotnet", "EmBrito.Aspire.Azure.CommunicationServices.Email.Emulator.dll"]
