FROM node:24.12.0-bookworm-slim AS build

# Set the working directory inside the container
WORKDIR /app

# Copy package-related files first to leverage Docker's caching mechanism
COPY package.json package-lock.json* ./

# Install project dependencies
RUN npm install

# Copy the rest of the application source code into the container
COPY . .

RUN npm run build

FROM node:24.12.0-bookworm-slim 

WORKDIR /app

COPY --from=build /app/.output ./.output

ENV PORT=3000
ENV HOST=0.0.0.0

EXPOSE 3000

CMD ["node", ".output/server/index.mjs"]
