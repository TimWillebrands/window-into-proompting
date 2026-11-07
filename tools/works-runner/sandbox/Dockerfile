# Build through build.sh: the context is a clean export of the repo, so no untracked secret can reach an image.
ARG SDK=mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview
FROM node:24-bookworm-slim AS node

FROM ${SDK}
ARG OPENCODE_VERSION=2.2.5
ARG PNPM_VERSION=10.8.0

COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && npm install -g pnpm@${PNPM_VERSION} \
    && curl -fsSL https://github.com/anomalyco/opencode/releases/download/v${OPENCODE_VERSION}/opencode-linux-x64.tar.gz \
       | tar -xz -C /usr/local/bin opencode

ENV NUGET_PACKAGES=/opt/nuget \
    npm_config_store_dir=/opt/pnpm-store \
    NuGetAudit=false \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    OPENCODE_DISABLE_MODELS_FETCH=1 \
    OPENCODE_DISABLE_LSP_DOWNLOAD=1 \
    OPENCODE_DISABLE_AUTOUPDATE=1

# Dependencies are frozen at build: there is no registry at run time.
COPY . /bake
RUN cd /bake && dotnet restore backend-test/backend-test.csproj \
    && cd frontend && pnpm fetch --frozen-lockfile \
    && rm -rf /bake \
    && chmod -R a+rwX /opt/nuget /opt/pnpm-store

COPY --chmod=755 tools/works-runner/sandbox/works-entry tools/works-runner/sandbox/works-checks /usr/local/bin/
WORKDIR /work
