FROM debian:bookworm-slim

WORKDIR /app

RUN apt-get update && apt-get install -y \
    gnupg ca-certificates curl apt-transport-https \
    && apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF \
    && echo "deb https://download.mono-project.com/repo/debian stable-buster main" > /etc/apt/sources.list.d/mono-official.list \
    && apt-get update && apt-get install -y \
    mono-complete \
    gtk-sharp2 \
    libgtk2.0-0 \
    libmono-posix4.0-cil \
    x11-apps \
    libxext6 \
    libxrender1 \
    libxtst6 \
    libxi6 \
    && rm -rf /var/lib/apt/lists/*

COPY . .

RUN find ClarionApp -name "*.cs" \
    ! -name "Window.cs" \
    ! -name "other.cs" \
    ! -name "generated.cs" \
    | tr '\n' ' ' | \
    xargs mcs -r:lib/ClarionLibrary.dll -pkg:gtk-sharp-2.0 -r:Mono.Posix -out:ClarionApp.exe
    

ENV MONO_PATH=/app/lib
CMD ["mono", "ClarionApp.exe"]