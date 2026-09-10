FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json TaskApi.sln TaskApi.csproj ./
COPY TaskApi.Tests/TaskApi.Tests.csproj TaskApi.Tests/
RUN dotnet restore TaskApi.sln

COPY . ./
RUN dotnet test TaskApi.sln -c Release --no-restore
RUN dotnet publish TaskApi.csproj -c Release -o /app/publish --no-restore
RUN for asset in login.css google.html google.js demo.js task-details.js task-details.css companion-demo.js companion-work.js companion-work.css team-billing.js team-billing.css task-workflow.js task-workflow.css task-inbox.js; do test -s "/app/publish/frontend/$asset"; done
RUN for pose in idle pet hat bow; do test -s "/app/publish/frontend/assets/pet/portfolio-cat-$pose-v3.png"; done
RUN test -s /app/publish/frontend/assets/brand/google-g.png
RUN for species in dog cat rabbit fox panda dragon; do test -s "/app/publish/frontend/assets/pet/portfolio-$species-atlas-v2-alpha.png"; for level in 1 2 3 4 5; do for kind in hat bow mat; do test -s "/app/publish/frontend/assets/pet/rewards-v2/$species/lv-$level-$kind.png"; done; done; done
RUN for folder in sources drafts archive; do test ! -d "/src/frontend/assets/pet/$folder"; test ! -d "/app/publish/frontend/assets/pet/$folder"; done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "TaskApi.dll"]
