# Cross-language compat dev image for the Vertex spec repo, built on a clean
# Debian base. Both language toolchains come from their own official apt repos
# — nothing is hand-stuffed into another project's image:
#   - .NET SDK 8.0   <- packages.microsoft.com   (runs dotnet-server)
#   - PHP 8.3 + Swoole <- packages.sury.org      (runs php-client)
#   - protoc, Go     <- Debian main
#
# php-client and dotnet-server talk over 127.0.0.1 inside this one container,
# so no host/container network bridging is involved.
FROM debian:bookworm

ARG DEBIAN_FRONTEND=noninteractive

# Common prerequisites for adding the two third-party apt repos.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl gnupg apt-transport-https lsb-release \
    && rm -rf /var/lib/apt/lists/*

# --- .NET SDK 8.0 from the Microsoft package repo ---
RUN curl -sSL https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -o /tmp/ms-prod.deb \
    && dpkg -i /tmp/ms-prod.deb \
    && rm /tmp/ms-prod.deb \
    && apt-get update && apt-get install -y --no-install-recommends dotnet-sdk-8.0 \
    && rm -rf /var/lib/apt/lists/*

# --- PHP 8.3 from the Sury repo, incl. the extensions pecl/swoole need ---
# php8.3-xml is required or `pecl install` dies with "XML Extension not found".
RUN curl -sSL https://packages.sury.org/php/apt.gpg -o /usr/share/keyrings/sury-php.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/sury-php.gpg] https://packages.sury.org/php/ bookworm main" \
        > /etc/apt/sources.list.d/sury-php.list \
    && apt-get update && apt-get install -y --no-install-recommends \
        php8.3-cli php8.3-dev php8.3-xml php8.3-mbstring php8.3-curl \
        php-pear pkg-config make gcc g++ \
        libssl-dev zlib1g-dev libbrotli-dev \
    && rm -rf /var/lib/apt/lists/*

# --- ext-swoole (OpenSSL + HTTP/2 on for the coroutine HTTP/2 client) ---
RUN pecl install -D 'enable-openssl="yes" enable-http2="yes" enable-swoole-curl="no"' swoole \
    && echo "extension=swoole.so" > /etc/php/8.3/cli/conf.d/20-swoole.ini \
    && php -m | grep -qi swoole

# --- protoc + Go from Debian main ---
RUN apt-get update && apt-get install -y --no-install-recommends \
        protobuf-compiler golang-go git unzip \
    && rm -rf /var/lib/apt/lists/*

# --- Composer (official binary) ---
COPY --from=composer:2 /usr/bin/composer /usr/local/bin/composer

WORKDIR /workspaces/Vertex
