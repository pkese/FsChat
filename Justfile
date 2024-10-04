PKGS := 'nuget.local'

nuget-shell:
    docker run -it --mount type=bind,source="$(pwd)",target=/app mono:latest bash

bundle:
    cd src/FsChat.Interactive/js-kernel && npm run build

nuget-local:
    dotnet publish
    dotnet pack
    mkdir -p {{PKGS}}
    sudo rm -rf {{PKGS}}/*
    docker run -it --mount type=bind,source="$(pwd)",target=/app mono:latest bash -c '\
        nuget init /app/src/FsChat.Interactive/bin/Release /app/{{PKGS}}; \
        nuget init /app/src/FsChat/bin/Release /app/{{PKGS}} '
    sudo chown -R $(whoami) {{PKGS}}
    mkdir {{PKGS}}/unzipped
    cd {{PKGS}}/unzipped && unzip ../fschat.interactive/0.0.1/fschat.interactive.0.0.1.nupkg
    # delete it from nuget cache
    rm -rf ~/.nuget/packages/fschat ~/.nuget/packages/fschat.interactive
