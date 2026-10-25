FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/src/Finance.Domain/Finance.Domain.csproj backend/src/Finance.Domain/
COPY backend/src/Finance.Application/Finance.Application.csproj backend/src/Finance.Application/
COPY backend/src/Finance.Infrastructure/Finance.Infrastructure.csproj backend/src/Finance.Infrastructure/
COPY backend/src/Finance.Api/Finance.Api.csproj backend/src/Finance.Api/
RUN dotnet restore backend/src/Finance.Api/Finance.Api.csproj
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/Finance.Api/Finance.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Finance.Api.dll"]
