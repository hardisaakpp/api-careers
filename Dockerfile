# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first so the restore layer is cached until a dependency changes.
COPY global.json Careers.slnx ./
COPY src/Careers.Domain/*.csproj src/Careers.Domain/
COPY src/Careers.Application/*.csproj src/Careers.Application/
COPY src/Careers.Infrastructure/*.csproj src/Careers.Infrastructure/
COPY src/Careers.Api/*.csproj src/Careers.Api/
RUN dotnet restore src/Careers.Api/Careers.Api.csproj
COPY src ./src
RUN dotnet publish src/Careers.Api/Careers.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out ./
# The aspnet image ships a non-root `app` user.
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Careers.Api.dll"]
