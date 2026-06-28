<?php

// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// Autoloader for the compat php-client. Like go-client's `replace` directive
// (and cpp-client's add_subdirectory), this references the sibling vertex-php
// SDK directly rather than depending on anything of its own — the SDK owns its
// dependencies (google/protobuf et al.), the compat client just uses them.
//
// The sibling SDK's composer autoloader provides BOTH the google/protobuf
// runtime AND the Vertex\ SDK classes. On top of it we register only the
// generated message classes that live next to this client (./gen).

declare(strict_types=1);

$here = __DIR__;
$workspace = \dirname($here, 4); // .../compat/hello-rpc/php-client → workspace root
$sdkRoot = $workspace . '/vertex-php';

if (!\is_dir($sdkRoot)) {
    fwrite(STDERR, "error: clone dengxuan/vertex-php at {$sdkRoot}\n");
    exit(1);
}

// The SDK's composer autoload covers google/protobuf and the Vertex\ namespace.
// The SDK owns its dependencies; run `composer install` in vertex-php if missing.
$sdkAutoload = $sdkRoot . '/vendor/autoload.php';
if (!\is_file($sdkAutoload)) {
    fwrite(STDERR, "error: run 'composer install' in {$sdkRoot} (the SDK owns its deps)\n");
    exit(1);
}
require $sdkAutoload;

// PSR-4 for the protoc-generated message classes that ship with this client.
spl_autoload_register(static function (string $class) use ($here): void {
    $map = [
        'Vertex\\Compat\\HelloRpc\\V1\\' => $here . '/gen/Vertex/Compat/HelloRpc/V1/',
        'GPBMetadata\\'                  => $here . '/gen/GPBMetadata/',
    ];
    foreach ($map as $prefix => $baseDir) {
        if (\str_starts_with($class, $prefix)) {
            $relative = \substr($class, \strlen($prefix));
            $file = $baseDir . \str_replace('\\', '/', $relative) . '.php';
            if (\is_file($file)) {
                require $file;
            }
            return;
        }
    }
});
