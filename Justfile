PKGS_DIR := 'nuget.local'

# extract <Version>0.0.1</Version> from src/FsChat/FsChat.fsproj
FSCHAT_VER := `grep -oPm1 "(?<=<Version>)[^<]+" src/FsChat/FsChat.fsproj`
FSCHAT_INTERACTIVE_VER := `grep -oPm1 "(?<=<Version>)[^<]+" src/FsChat/FsChat.fsproj`

# load NUGET_AP_KEY from .env file
NUGET_AP_KEY := `cat .env | grep NUGET_AP_KEY | cut -d '=' -f 2 | tr -d '"'`

all: restore bundle nuget-local

restore:
    dotnet restore
    cd src/FsChat.Interactive/js-kernel && npm install

bundle:
    cd src/FsChat.Interactive/js-kernel && npm run build

nupkg:
    @echo "Versions: FsChat='{{FSCHAT_VER}}' FsChat.Interactive='{{FSCHAT_INTERACTIVE_VER}}'"
    @#echo "NUGET_API_KEY: '{{NUGET_AP_KEY}}'"
    dotnet publish
    #dotnet pack
    mkdir -p {{PKGS_DIR}}
    sudo rm -rf {{PKGS_DIR}}/*
    docker run -it --mount type=bind,source="$(pwd)",target=/app mono:latest bash -c '\
        nuget init /app/src/FsChat.Interactive/bin/Release /app/{{PKGS_DIR}}; \
        nuget init /app/src/FsChat/bin/Release /app/{{PKGS_DIR}} '
    sudo chown -R $(whoami) {{PKGS_DIR}}

nuget-local: nupkg
    mkdir -p {{PKGS_DIR}}/unzipped/fschat {{PKGS_DIR}}/unzipped/fschat.interactive
    cd {{PKGS_DIR}}/unzipped/fschat.interactive && unzip ../../fschat.interactive/{{FSCHAT_INTERACTIVE_VER}}/fschat.interactive.{{FSCHAT_INTERACTIVE_VER}}.nupkg
    cd {{PKGS_DIR}}/unzipped/fschat && unzip ../../fschat/{{FSCHAT_INTERACTIVE_VER}}/fschat.{{FSCHAT_INTERACTIVE_VER}}.nupkg
    # delete it from nuget cache
    rm -rf ~/.nuget/packages/fschat ~/.nuget/packages/fschat.interactive

nuget-push:
    dotnet nuget push {{PKGS_DIR}}/fschat/{{FSCHAT_VER}}/fschat.{{FSCHAT_VER}}.nupkg                         --api-key {{NUGET_AP_KEY}} --source https://api.nuget.org/v3/index.json
    dotnet nuget push {{PKGS_DIR}}/fschat.interactive/{{FSCHAT_INTERACTIVE_VER}}/fschat.interactive.{{FSCHAT_VER}}.nupkg                         --api-key {{NUGET_AP_KEY}} --source https://api.nuget.org/v3/index.json
