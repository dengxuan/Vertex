<?php

// Licensed to the Gordon under one or more agreements.
// Gordon licenses this file to you under the MIT license.

// Minimal autoloader for the compat php-client. Wires up three roots without
// requiring a composer install of the sibling vertex-php SDK (mirrors how
// go-client uses a `replace` directive and cpp-client uses add_subdirectory to
// reference the sibling implementation directly while the spec repo is the
// source of truth).
//
//   1. google/protobuf runtime  — must already be installed via composer here
//   2. generated HelloEvent      — ./gen (protoc --php_out output)
//   3. vertex-php SDK            — ../../../../vertex-php/src (sibling clone)

declare(strict_types=1);

$here = __DIR__;
$workspace = \dirname($here, 4); // .../compat/hello/php-client → workspace root
$sdkSrc = $workspace . '/vertex-php/src';

if (!\is_dir($sdkSrc)) {
    fwrite(STDERR, "error: clone dengxuan/vertex-php at {$workspace}/vertex-php\n");
    exit(1);
}

// google/protobuf runtime: installed into ./vendor by run-php.sh's composer step.
$protobufAutoload = $here . '/vendor/autoload.php';
if (!\is_file($protobufAutoload)) {
    fwrite(STDERR, "error: run 'composer install' in php-client/ (needs google/protobuf)\n");
    exit(1);
}
require $protobufAutoload;

// PSR-4 for the generated classes (Vertex\Compat\HelloRpc\V1 → ./gen, plus the
// GPBMetadata bootstrap) and the SDK (Vertex\ → ../../../../vertex-php/src).
spl_autoload_register(static function (string $class) use ($here, $sdkSrc): void {
    $map = [
        'Vertex\\Compat\\HelloRpc\\V1\\' => $here . '/gen/Vertex/Compat/HelloRpc/V1/',
        'GPBMetadata\\'              => $here . '/gen/GPBMetadata/',
        'Vertex\\'                   => $sdkSrc . '/',
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
