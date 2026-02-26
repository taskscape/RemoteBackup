<?php
/**
 * Unified Backup Endpoint
 * Handles both Filesystem and Database backups.
 */

// --- DEBUG MODE (Keep it for a moment) ---
ini_set('display_errors', 1);
ini_set('display_startup_errors', 1);
error_reporting(E_ALL);
// ------------------

header('Content-Type: application/json');
set_time_limit(0);
ini_set('memory_limit', '512M');

$config = require 'config.php';

// 1. Authentication (Safe way to get token)
$providedToken = $_GET['token'] ?? null;

// Fallback to headers if not in URL
if (!$providedToken) {
    $headers = [];
    foreach ($_SERVER as $name => $value) {
        if (substr($name, 0, 5) == 'HTTP_') {
            $headers[str_replace(' ', '-', ucwords(strtolower(str_replace('_', ' ', substr($name, 5)))))] = $value;
        }
    }
    $providedToken = $headers['X-Backup-Token'] ?? null;
}

if (!$providedToken || $providedToken !== $config['auth_token']) {
    http_response_code(403);
    echo json_encode(['status' => 'error', 'message' => 'Unauthorized']);
    exit;
}

// 2. Setup Directory
if (!is_dir($config['backup_dir'])) {
    mkdir($config['backup_dir'], 0755, true);
}

// 3. Cleanup Old Backups
cleanupOldBackups($config['backup_dir'], $config['retention_days']);

// 4. Determine Action
$action = $_GET['action'] ?? null;

switch ($action) {
    case 'files':
        handleFilesBackup($config);
        break;
    case 'db':
        handleDatabaseBackup($config);
        break;
    default:
        echo json_encode([
            'status' => 'error', 
            'message' => 'Invalid action. Use action=files or action=db'
        ]);
        break;
}

// --- Functions ---

function cleanupOldBackups($dir, $days) {
    $files = glob($dir . '/*');
    $now = time();
    foreach ($files as $file) {
        if (is_file($file)) {
            if ($now - filemtime($file) >= $days * 24 * 60 * 60) {
                unlink($file);
            }
        }
    }
}

function handleFilesBackup($config) {
    $timestamp = date('Y-m-d_H-i-s');
    $filename = 'fs_backup_' . $timestamp . '.zip';
    $outputPath = $config['backup_dir'] . '/' . $filename;

    if (!class_exists('ZipArchive')) {
        echo json_encode(['status' => 'error', 'message' => 'ZipArchive class not found. Enable ZIP extension in PHP.']);
        return;
    }

    $zip = new ZipArchive();
    if ($zip->open($outputPath, ZipArchive::CREATE | ZipArchive::OVERWRITE) !== TRUE) {
        echo json_encode(['status' => 'error', 'message' => 'Could not create ZIP file']);
        return;
    }

    $sourceDir = $config['fs']['source_dir'];
    $excludeDirs = array_map(function($d) use ($sourceDir) {
        return realpath($sourceDir . DIRECTORY_SEPARATOR . $d);
    }, $config['fs']['exclude_dirs']);
    
    $excludeDirs[] = realpath($config['backup_dir']);

    $files = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($sourceDir, RecursiveDirectoryIterator::SKIP_DOTS),
        RecursiveIteratorIterator::SELF_FIRST
    );

    foreach ($files as $file) {
        $filePath = $file->getRealPath();
        $relativePath = substr($filePath, strlen($sourceDir) + 1);

        $shouldExclude = false;
        foreach ($excludeDirs as $exDir) {
            if ($exDir && strpos($filePath, $exDir) === 0) {
                $shouldExclude = true;
                break;
            }
        }

        if ($shouldExclude) continue;

        if ($file->isDir()) {
            $zip->addEmptyDir($relativePath);
        } else {
            $ext = pathinfo($filePath, PATHINFO_EXTENSION);
            if (in_array(strtolower($ext), $config['fs']['exclude_extensions'])) continue;
            
            $zip->addFile($filePath, $relativePath);
        }
    }

    $zip->close();

    if (file_exists($outputPath)) {
        echo json_encode([
            'status' => 'success',
            'action' => 'files',
            'file' => $filename,
            'size' => filesize($outputPath),
            'download_url' => getDownloadUrl($filename)
        ]);
    } else {
        echo json_encode(['status' => 'error', 'message' => 'ZIP creation failed']);
    }
}

function handleDatabaseBackup($config) {
    $dbCfg = $config['db'];
    $timestamp = date('Y-m-d_H-i-s');
    $filename = 'db_backup_' . $timestamp . '.sql';
    $outputPath = $config['backup_dir'] . '/' . $filename;

    try {
        if (!function_exists('mysqli_connect')) {
            throw new Exception('mysqli extension not found. Enable mysqli in PHP.');
        }

        mysqli_report(MYSQLI_REPORT_ERROR | MYSQLI_REPORT_STRICT);
        $mysqli = new mysqli($dbCfg['host'], $dbCfg['user'], $dbCfg['pass'], $dbCfg['name']);
        $mysqli->set_charset("utf8mb4");

        $handle = fopen($outputPath, 'w+');
        fwrite($handle, "-- Backup: " . date("Y-m-d H:i:s") . "\n\n");

        $tables = [];
        if ($dbCfg['table_prefix'] === '*') {
            $result = $mysqli->query("SHOW TABLES");
            while ($row = $result->fetch_row()) $tables[] = $row[0];
        } else {
            $escapedPrefix = $mysqli->real_escape_string($dbCfg['table_prefix']);
            $pattern = str_replace('_', '\_', $escapedPrefix) . '%';
            $result = $mysqli->query("SHOW TABLES LIKE '$pattern'");
            while ($row = $result->fetch_row()) $tables[] = $row[0];
        }

        foreach ($tables as $table) {
            $createRes = $mysqli->query("SHOW CREATE TABLE `$table`")->fetch_row();
            fwrite($handle, "DROP TABLE IF EXISTS `$table`;\n" . $createRes[1] . ";\n\n");

            $dataRes = $mysqli->query("SELECT * FROM `$table`", MYSQLI_USE_RESULT);
            while ($row = $dataRes->fetch_assoc()) {
                $vals = array_map(function($v) use ($mysqli) {
                    return is_null($v) ? "NULL" : "'" . $mysqli->real_escape_string($v) . "'";
                }, $row);
                fwrite($handle, "INSERT INTO `$table` VALUES (" . implode(', ', $vals) . ");\n");
            }
            fwrite($handle, "\n");
        }

        fclose($handle);
        $mysqli->close();

        echo json_encode([
            'status' => 'success',
            'action' => 'db',
            'file' => $filename,
            'size' => filesize($outputPath),
            'download_url' => getDownloadUrl($filename)
        ]);
    } catch (Exception $e) {
        echo json_encode(['status' => 'error', 'message' => $e->getMessage()]);
    }
}

function getDownloadUrl($filename) {
    $protocol = isset($_SERVER['HTTPS']) && $_SERVER['HTTPS'] === 'on' ? 'https' : 'http';
    $host = $_SERVER['HTTP_HOST'];
    $uri = dirname($_SERVER['REQUEST_URI']);
    return "$protocol://$host$uri/archives/$filename";
}
