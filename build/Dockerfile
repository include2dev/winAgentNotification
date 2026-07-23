# Builder image for producing the WinAgentNotification MSI on Linux CI.
#
#   docker build -t <registry>/build-images/dotnet-msi:10.0-wixl build/
#
# The base MUST be the Microsoft-built SDK image (mcr.microsoft.com).
# Distro source-built SDKs lack Sdks/Microsoft.NET.Sdk.WindowsDesktop and
# fail with MSB4019 on the App project's WindowsDesktop-targeted build.
#
# MSI tooling is msitools/wixl (Linux-native). WiX itself (any version,
# verified on 4.0.5/5.0.2/6.0.2) cannot build MSIs on non-Windows —
# Directory/@Name validation fails with WIX0389 — and WiX v7 additionally
# gates usage behind the OSMF maintenance-fee EULA.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      msitools \
      wixl \
      osslsigncode \
      ca-certificates \
 && rm -rf /var/lib/apt/lists/*
