FROM node:24.12.0-bookworm-slim AS build

WORKDIR /app

RUN npm install -g pnpm@10.8.0

COPY package.json pnpm-lock.yaml .npmrc ./

RUN pnpm install --frozen-lockfile

COPY . .

RUN pnpm run build

FROM node:24.12.0-bookworm-slim

WORKDIR /app

COPY --from=build /app/.output ./.output

ENV PORT=3000
ENV HOST=0.0.0.0

EXPOSE 3000

CMD ["node", ".output/server/index.mjs"]
