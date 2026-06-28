<?php

// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// Autoloader for the compat php-client. Like go-client's `replace` directive,
// this references the sibling vertex-php SDK directly and depends on nothing of
// its own — the SDK owns its dependencies (google/protobuf et al.). On top of
// the SDK's composer autoloader we register only the generated message classes.

declare(strict_types=1);

$here = __DIR__;
$workspace = \dirname($here, 4); // .../compat/bidi-echo/php-client → workspace root
$sdkRoot = $workspace . '/vertex-php';

if (!\is_dir($sdkRoot)) {
    fwrite(STDERR, "error: clone dengxuan/vertex-php at {$sdkRoot}\n");
    exit(1);
}

$sdkAutoload = $sdkRoot . '/vendor/autoload.php';
if (!\is_file($sdkAutoload)) {
    fwrite(STDERR, "error: run 'composer install' in {$sdkRoot} (the SDK owns its deps)\n");
    exit(1);
}
require $sdkAutoload;

spl_autoload_register(static function (string $class) use ($here): void {
    $map = [
        'Vertex\\Compat\\BidiEcho\\V1\\' => $here . '/gen/Vertex/Compat/BidiEcho/V1/',
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
