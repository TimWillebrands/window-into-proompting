# The CDU (ADR 0039): Pi in a Node service, with what it needs to build, test and open PRs on this repo.
# Context is the repo root; build from a clean checkout (Kamal does) so no untracked secret reaches the image.
ARG SDK=mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview
FROM node:24-bookworm-slim AS node

FROM ${SDK}
ARG PNPM_VERSION=10.8.0
ARG GH_VERSION=2.100.0

COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx \
    && npm install -g pnpm@${PNPM_VERSION} \
    && curl -fsSL https://github.com/cli/cli/releases/download/v${GH_VERSION}/gh_${GH_VERSION}_linux_amd64.tar.gz \
       | tar -xz -C /usr/local --strip-components=1 gh_${GH_VERSION}_linux_amd64/bin/gh \
    && apt-get update && apt-get install -y --no-install-recommends postgresql-client ripgrep jq && rm -rf /var/lib/apt/lists/* \
    && curl -LsSf https://astral.sh/uv/install.sh | env UV_INSTALL_DIR=/usr/local/bin sh \
    && UV_TOOL_DIR=/opt/uv-tools UV_TOOL_BIN_DIR=/usr/local/bin uv tool install postgres-mcp

ENV NUGET_PACKAGES=/opt/nuget \
    npm_config_store_dir=/opt/pnpm-store \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    GIT_COMMITTER_NAME=CDU \
    GIT_COMMITTER_EMAIL=cdu@proompting.party \
    CDU_HOME=/cdu

# Warm caches so a first build doesn't wait on the network; the CDU can still add packages.
COPY . /bake
RUN cd /bake && dotnet restore backend-test/backend-test.csproj \
    && cd frontend && pnpm fetch --frozen-lockfile \
    && rm -rf /bake

WORKDIR /app
COPY tools/cdu/package.json tools/cdu/package-lock.json ./
RUN npm ci --omit=dev --ignore-scripts
COPY tools/cdu/src ./src
COPY tools/cdu/book ./book
COPY tools/cdu/soul ./soul
COPY --chmod=755 tools/cdu/cdu-entry /usr/local/bin/cdu-entry

VOLUME /cdu
EXPOSE 8790
ENTRYPOINT ["cdu-entry"]
