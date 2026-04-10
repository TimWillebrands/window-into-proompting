FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0-preview AS build
WORKDIR /src
COPY . .
RUN dotnet publish backend.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0-preview
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5072
ENV ASPNETCORE_URLS=http://+:5072
ENTRYPOINT ["dotnet", "backend.dll"]
