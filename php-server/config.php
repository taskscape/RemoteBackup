<?php
/**
 * Global Configuration for RemoteBackup PHP Script
 */

return [
    // Security token that must be sent in the X-Backup-Token header
    'auth_token' => 'CHANGE_ME_TO_A_SECURE_RANDOM_STRING',

    // Directory where backup files will be stored (relative to this script or absolute)
    'backup_dir' => __DIR__ . '/archives',

    // How many days to keep old backup files
    'retention_days' => 7,

    // Database configuration
    'db' => [
        'host' => 'localhost',
        'user' => 'root',
        'pass' => '',
        'name' => 'database_name',
        'table_prefix' => '*', // Use '*' for all tables, or 'wp_' for specific prefix
    ],

    // Filesystem configuration
    'fs' => [
        'source_dir' => realpath(__DIR__ . '/../'), // Directory to backup (default is one level up)
        'exclude_dirs' => [
            'archives', // Exclude the backup storage directory
            '.git',
            'node_modules',
            'vendor'
        ],
        'exclude_extensions' => ['zip', 'sql', 'log', 'bak']
    ]
];
