# DEPRECATED — Use CopyPaste Pro instead (dist\CopyPastePro.exe or run .\build.ps1)
# This script only saved clipboard images into date folders; it was not a full clipboard history tool.
#
# Image Organizer Script with System Tray
# Monitors directory for image files and organizes them by date
# Supports both directory-based and clipboard-based modes

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Media
Add-Type -AssemblyName System.Security
Add-Type -AssemblyName Microsoft.VisualBasic


<#


# Hide console window - run in background
# Only hide if console window exists (not when run from ISE or double-clicked)
try {
    Add-Type -Name Window -Namespace Console -MemberDefinition '
    [DllImport("Kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, Int32 nCmdShow);
    ' -ErrorAction SilentlyContinue
    
    $consolePtr = [Console.Window]::GetConsoleWindow()
    if ($consolePtr -ne [IntPtr]::Zero) {
        # Hide the window (0 = SW_HIDE)
        [Console.Window]::ShowWindow($consolePtr, 0) | Out-Null
    }
}
catch {
    # If hiding fails, continue anyway - script will still work
}


#>


# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$scriptDir = if ($scriptDir) { $scriptDir } else { Get-Location }

# Image file extensions to monitor
$imageExtensions = @('.jpg', '.jpeg', '.png', '.gif', '.bmp', '.tiff', '.tif', '.webp', '.ico', '.svg')

# State variables
$isPaused = $false
$totalProcessed = 0
$processedFiles = @{}
$mode = "Clipboard"  # "Directory" or "Clipboard"
$lastClipboardHash = $null
$clipboardTimer = $null

# Configurable intervals (in milliseconds)
$fileProcessingDelay = 100  # Delay before processing file after detection
$clipboardCheckInterval = 200  # Interval for checking clipboard

# Notification preferences
$notifySettingsChanges = $true  # Notifications when changing settings
$notifyImageProcessing = $false  # Notifications when images are copied/processed
$notifyStatusChanges = $true     # Notifications for pause/resume, mode switches
$notifyArchiveProgress = $true  # Notifications for archiving operations

# Create NotifyIcon for system tray
$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = [System.Drawing.SystemIcons]::Information
$notifyIcon.Text = "Image Organizer"
$notifyIcon.Visible = $true

# Function to play sound
function Play-Sound {
    param (
        [int]$frequency = 800,
        [int]$duration = 200
    )
    try {
        [Console]::Beep($frequency, $duration)
    }
    catch {
        # Fallback if beep doesn't work
        try {
            [System.Media.SystemSounds]::Asterisk.Play()
        }
        catch { }
    }
}

# Function to update icon based on state
function Update-Icon {
    $modeText = if ($mode -eq "Clipboard") { "Clipboard" } else { "Directory" }
    $statusText = if ($isPaused) { "Paused" } else { "Active" }
    
    if ($isPaused) {
        $notifyIcon.Icon = [System.Drawing.SystemIcons]::Warning
        $notifyIcon.Text = "Image Organizer - $modeText Mode ($statusText)"
    }
    else {
        $notifyIcon.Icon = [System.Drawing.SystemIcons]::Information
        $notifyIcon.Text = "Image Organizer - $modeText Mode ($statusText)"
    }
}

# Function to get current state info
function Get-CurrentState {
    $dateFolders = Get-ChildItem -Path $scriptDir -Directory | Where-Object { 
        $_.Name -match '^\d{4}-\d{2}-\d{2}$' 
    }
    
    $totalFolders = $dateFolders.Count
    $totalImages = 0
    $emptyFolders = 0
    
    foreach ($folder in $dateFolders) {
        $images = Get-ChildItem -Path $folder.FullName -File -ErrorAction SilentlyContinue
        $imageCount = ($images | Where-Object { $imageExtensions -contains $_.Extension.ToLower() }).Count
        $totalImages += $imageCount
        if ($imageCount -eq 0) {
            $emptyFolders++
        }
    }
    
    $status = if ($isPaused) { "Paused" } else { "Active" }
    
    return @{
        Status = $status
        Mode = $mode
        TotalProcessed = $totalProcessed
        DateFolders = $totalFolders
        TotalImages = $totalImages
        EmptyFolders = $emptyFolders
        MonitoringPath = $scriptDir
    }
}

# Function to show current state
function Show-CurrentState {
    $state = Get-CurrentState
    $message = @"
Image Organizer Status

Mode: $($state.Mode)
Status: $($state.Status)
Total Images Processed: $($state.TotalProcessed)
Date Folders: $($state.DateFolders)
Total Images in Folders: $($state.TotalImages)
Empty Folders: $($state.EmptyFolders)
Monitoring Path: $($state.MonitoringPath)
"@
    
    [System.Windows.Forms.MessageBox]::Show($message, "Image Organizer - Current State", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
}

# Function to archive folders
function Archive-Folders {
    $dateFolders = Get-ChildItem -Path $scriptDir -Directory | Where-Object { 
        $_.Name -match '^\d{4}-\d{2}-\d{2}$' 
    }
    
    if ($dateFolders.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("No date folders found to archive.", "Archive", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
        return
    }
    
    # Filter out empty folders and non-date folders
    $foldersToArchive = @()
    $skippedFolders = @()
    
    foreach ($folder in $dateFolders) {
        $images = Get-ChildItem -Path $folder.FullName -File -ErrorAction SilentlyContinue
        $imageCount = ($images | Where-Object { $imageExtensions -contains $_.Extension.ToLower() }).Count
        
        if ($imageCount -eq 0) {
            $skippedFolders += $folder.Name
        }
        else {
            $foldersToArchive += $folder
        }
    }
    
    if ($foldersToArchive.Count -eq 0) {
        $msg = "No folders with images found to archive."
        if ($skippedFolders.Count -gt 0) {
            $msg += "`n`nSkipped empty folders: $($skippedFolders.Count)"
        }
        [System.Windows.Forms.MessageBox]::Show($msg, "Archive", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
        return
    }
    
    # Ask for confirmation
    $confirmMsg = "Archive $($foldersToArchive.Count) folder(s)?`n`nFolders to archive:`n"
    $confirmMsg += ($foldersToArchive.Name -join "`n")
    if ($skippedFolders.Count -gt 0) {
        $confirmMsg += "`n`nSkipped empty folders: $($skippedFolders.Count)"
    }
    
    $result = [System.Windows.Forms.MessageBox]::Show($confirmMsg, "Archive Confirmation", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question)
    
    if ($result -eq [System.Windows.Forms.DialogResult]::Yes) {
        try {
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            
            # Check for 7zip CLI first
            $7zipPath = $null
            $7zipLocations = @(
                "${env:ProgramFiles}\7-Zip\7z.exe",
                "${env:ProgramFiles(x86)}\7-Zip\7z.exe",
                "$env:LOCALAPPDATA\Programs\7-Zip\7z.exe"
            )
            
            foreach ($location in $7zipLocations) {
                if (Test-Path $location) {
                    $7zipPath = $location
                    break
                }
            }
            
            # Try to find 7zip in PATH
            if (-not $7zipPath) {
                $7zipInPath = Get-Command 7z.exe -ErrorAction SilentlyContinue
                if ($7zipInPath) {
                    $7zipPath = $7zipInPath.Source
                }
            }
            
            if ($7zipPath) {
                # Use 7zip for better compression
                $archiveName = "archive-$timestamp.7z"
                $archivePath = Join-Path $scriptDir $archiveName
                
                if ($notifyArchiveProgress) {
                    $notifyIcon.BalloonTipText = "Archiving with 7-Zip..."
                    $notifyIcon.ShowBalloonTip(2000)
                }
                
                # Build 7zip command arguments (no quotes needed, PowerShell handles it)
                $7zipArgs = @(
                    "a",
                    "-t7z",
                    $archivePath
                )
                
                # Add each folder to archive
                foreach ($folder in $foldersToArchive) {
                    $7zipArgs += $folder.FullName
                }
                
                $process = Start-Process -FilePath $7zipPath -ArgumentList $7zipArgs -Wait -NoNewWindow -PassThru
                
                if ($process.ExitCode -eq 0 -and (Test-Path $archivePath)) {
                    if ($notifyArchiveProgress) {
                        $notifyIcon.BalloonTipText = "Archive created: $archiveName"
                        $notifyIcon.ShowBalloonTip(3000)
                    }
                }
                else {
                    throw "7-Zip archive creation failed (Exit code: $($process.ExitCode))"
                }
            }
            else {
                # Use PowerShell's built-in compression
                $archiveName = "archive-$timestamp.zip"
                $archivePath = Join-Path $scriptDir $archiveName
                
                if ($notifyArchiveProgress) {
                    $notifyIcon.BalloonTipText = "Archiving folders..."
                    $notifyIcon.ShowBalloonTip(2000)
                }
                
                $filesToArchive = @()
                foreach ($folder in $foldersToArchive) {
                    $files = Get-ChildItem -Path $folder.FullName -File -Recurse
                    foreach ($file in $files) {
                        $filesToArchive += $file.FullName
                    }
                }
                
                Compress-Archive -Path $filesToArchive -DestinationPath $archivePath -Force
                
                if ($notifyArchiveProgress) {
                    $notifyIcon.BalloonTipText = "Archive created: $archiveName"
                    $notifyIcon.ShowBalloonTip(3000)
                }
            }
            
            # Ask if user wants to delete archived folders
            $deleteResult = [System.Windows.Forms.MessageBox]::Show(
                "Archive created successfully.`n`nDelete archived folders?",
                "Delete Folders?",
                [System.Windows.Forms.MessageBoxButtons]::YesNo,
                [System.Windows.Forms.MessageBoxIcon]::Question
            )
            
            if ($deleteResult -eq [System.Windows.Forms.DialogResult]::Yes) {
                foreach ($folder in $foldersToArchive) {
                    Remove-Item -Path $folder.FullName -Recurse -Force
                }
                if ($notifyArchiveProgress) {
                    $notifyIcon.BalloonTipText = "Archived folders deleted."
                    $notifyIcon.ShowBalloonTip(2000)
                }
            }
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show("Error creating archive: $_", "Archive Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        }
    }
}

# Create context menu with submenu
$contextMenu = New-Object System.Windows.Forms.ContextMenuStrip

# Current State menu item
$stateMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$stateMenuItem.Text = "Current State"
$stateMenuItem.Add_Click({
    Show-CurrentState
})
$contextMenu.Items.Add($stateMenuItem) | Out-Null

# Separator
$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# Mode toggle menu item
$modeMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
function Update-ModeMenuItem {
    if ($mode -eq "Clipboard") {
        $modeMenuItem.Text = "Switch to Directory Mode"
    }
    else {
        $modeMenuItem.Text = "Switch to Clipboard Mode"
    }
}
Update-ModeMenuItem

$modeMenuItem.Add_Click({
    if ($mode -eq "Clipboard") {
        $script:mode = "Directory"
        # Stop clipboard monitoring
        if ($clipboardTimer) {
            $clipboardTimer.Stop()
            $clipboardTimer.Dispose()
            $clipboardTimer = $null
        }
        # Start directory monitoring
        $watcher.EnableRaisingEvents = -not $isPaused
    }
    else {
        $script:mode = "Clipboard"
        # Stop directory monitoring
        $watcher.EnableRaisingEvents = $false
        # Start clipboard monitoring
        Start-ClipboardMonitoring
    }
    Update-ModeMenuItem
    Update-Icon
    
    if ($notifyStatusChanges) {
        $notifyIcon.BalloonTipText = "Switched to $mode mode"
        $notifyIcon.ShowBalloonTip(2000)
    }
})
$contextMenu.Items.Add($modeMenuItem) | Out-Null

# Separator
$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# Pause/Resume menu item
$pauseMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
function Update-PauseMenuItem {
    if ($isPaused) {
        $pauseMenuItem.Text = "Resume"
    }
    else {
        $pauseMenuItem.Text = "Pause"
    }
}
Update-PauseMenuItem

$pauseMenuItem.Add_Click({
    $script:isPaused = -not $script:isPaused
    
    if ($mode -eq "Directory") {
        $watcher.EnableRaisingEvents = -not $script:isPaused
    }
    else {
        if ($script:isPaused) {
            if ($clipboardTimer) {
                $clipboardTimer.Stop()
            }
        }
        else {
            Start-ClipboardMonitoring
        }
    }
    
    Update-PauseMenuItem
    Update-Icon
    
    if ($notifyStatusChanges) {
        if ($script:isPaused) {
            $notifyIcon.BalloonTipText = "Monitoring paused"
        }
        else {
            $notifyIcon.BalloonTipText = "Monitoring resumed"
        }
        $notifyIcon.ShowBalloonTip(2000)
    }
})
$contextMenu.Items.Add($pauseMenuItem) | Out-Null

# Separator
$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# Archive menu item
$archiveMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$archiveMenuItem.Text = "Archive Folders"
$archiveMenuItem.Add_Click({
    Archive-Folders
})
$contextMenu.Items.Add($archiveMenuItem) | Out-Null

# Separator
$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# Settings submenu
$settingsMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$settingsMenuItem.Text = "Settings"

# Function to update settings menu text
function Update-SettingsMenu {
    $fileDelayMenuItem.Text = "File Processing Delay: $fileProcessingDelay ms"
    $clipboardIntervalMenuItem.Text = "Clipboard Check Interval: $clipboardCheckInterval ms"
    Update-NotificationMenu
}

# Function to update notification menu checkmarks
function Update-NotificationMenu {
    $notifySettingsMenuItem.Checked = $notifySettingsChanges
    $notifyImageMenuItem.Checked = $notifyImageProcessing
    $notifyStatusMenuItem.Checked = $notifyStatusChanges
    $notifyArchiveMenuItem.Checked = $notifyArchiveProgress
}

# File Processing Delay setting
$fileDelayMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$fileDelayMenuItem.Text = "File Processing Delay: $fileProcessingDelay ms"
$fileDelayMenuItem.Add_Click({
    $result = [Microsoft.VisualBasic.Interaction]::InputBox(
        "Enter file processing delay in milliseconds (default: 350):`n`nMinimum: 100ms, Maximum: 5000ms",
        "File Processing Delay",
        $fileProcessingDelay
    )
    if ($result -ne "" -and $result -match '^\d+$') {
        $newValue = [int]$result
        if ($newValue -lt 100) { $newValue = 100 }
        if ($newValue -gt 5000) { $newValue = 5000 }
        $script:fileProcessingDelay = $newValue
        Update-SettingsMenu
        if ($notifySettingsChanges) {
            $notifyIcon.BalloonTipText = "File processing delay set to $fileProcessingDelay ms"
            $notifyIcon.ShowBalloonTip(2000)
        }
    }
})
$settingsMenuItem.DropDownItems.Add($fileDelayMenuItem) | Out-Null

# Clipboard Check Interval setting
$clipboardIntervalMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$clipboardIntervalMenuItem.Text = "Clipboard Check Interval: $clipboardCheckInterval ms"
$clipboardIntervalMenuItem.Add_Click({
    $result = [Microsoft.VisualBasic.Interaction]::InputBox(
        "Enter clipboard check interval in milliseconds (default: 500):`n`nMinimum: 100ms, Maximum: 10000ms",
        "Clipboard Check Interval",
        $clipboardCheckInterval
    )
    if ($result -ne "" -and $result -match '^\d+$') {
        $newValue = [int]$result
        if ($newValue -lt 100) { $newValue = 100 }
        if ($newValue -gt 10000) { $newValue = 10000 }
        $script:clipboardCheckInterval = $newValue
        Update-SettingsMenu
        
        # Restart clipboard monitoring with new interval if active
        if ($mode -eq "Clipboard" -and -not $isPaused) {
            Start-ClipboardMonitoring
        }
        
        if ($notifySettingsChanges) {
            $notifyIcon.BalloonTipText = "Clipboard check interval set to $clipboardCheckInterval ms"
            $notifyIcon.ShowBalloonTip(2000)
        }
    }
})
$settingsMenuItem.DropDownItems.Add($clipboardIntervalMenuItem) | Out-Null

# Separator in settings menu
$settingsMenuItem.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# Notification preferences submenu
$notifySubMenu = New-Object System.Windows.Forms.ToolStripMenuItem
$notifySubMenu.Text = "Notifications"

# Settings change notifications
$notifySettingsMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$notifySettingsMenuItem.Text = "Settings Changes"
$notifySettingsMenuItem.Checked = $notifySettingsChanges
$notifySettingsMenuItem.Add_Click({
    $script:notifySettingsChanges = -not $script:notifySettingsChanges
    Update-NotificationMenu
})
$notifySubMenu.DropDownItems.Add($notifySettingsMenuItem) | Out-Null

# Image processing notifications
$notifyImageMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$notifyImageMenuItem.Text = "Image Processing"
$notifyImageMenuItem.Checked = $notifyImageProcessing
$notifyImageMenuItem.Add_Click({
    $script:notifyImageProcessing = -not $script:notifyImageProcessing
    Update-NotificationMenu
})
$notifySubMenu.DropDownItems.Add($notifyImageMenuItem) | Out-Null

# Status change notifications
$notifyStatusMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$notifyStatusMenuItem.Text = "Status Changes"
$notifyStatusMenuItem.Checked = $notifyStatusChanges
$notifyStatusMenuItem.Add_Click({
    $script:notifyStatusChanges = -not $script:notifyStatusChanges
    Update-NotificationMenu
})
$notifySubMenu.DropDownItems.Add($notifyStatusMenuItem) | Out-Null

# Archive progress notifications
$notifyArchiveMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$notifyArchiveMenuItem.Text = "Archive Progress"
$notifyArchiveMenuItem.Checked = $notifyArchiveProgress
$notifyArchiveMenuItem.Add_Click({
    $script:notifyArchiveProgress = -not $script:notifyArchiveProgress
    Update-NotificationMenu
})
$notifySubMenu.DropDownItems.Add($notifyArchiveMenuItem) | Out-Null

$settingsMenuItem.DropDownItems.Add($notifySubMenu) | Out-Null

# Initialize notification menu
Update-NotificationMenu

# Add Settings menu to context menu
$contextMenu.Items.Add($settingsMenuItem) | Out-Null

# Separator
$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# Open Directory menu item
$openDirMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$openDirMenuItem.Text = "Open Directory"
$openDirMenuItem.Add_Click({
    try {
        Start-Process explorer.exe -ArgumentList $scriptDir
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show("Error opening directory: $_", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    }
})
$contextMenu.Items.Add($openDirMenuItem) | Out-Null

# Separator
$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# Exit menu item
$exitMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$exitMenuItem.Text = "Exit"
$exitMenuItem.Add_Click({
    $notifyIcon.Visible = $false
    [System.Windows.Forms.Application]::Exit()
})
$contextMenu.Items.Add($exitMenuItem) | Out-Null

$notifyIcon.ContextMenuStrip = $contextMenu

# Verify Settings menu is properly added
if ($settingsMenuItem.DropDownItems.Count -eq 0) {
    # If settings submenu is empty, add items again
    $settingsMenuItem.DropDownItems.Add($fileDelayMenuItem) | Out-Null
    $settingsMenuItem.DropDownItems.Add($clipboardIntervalMenuItem) | Out-Null
}

# Show initial notification (always show on startup)
$notifyIcon.BalloonTipTitle = "Image Organizer"
$notifyIcon.BalloonTipText = "Monitoring directory: $scriptDir"
$notifyIcon.ShowBalloonTip(3000)

# Function to check clipboard for images
function Check-Clipboard {
    try {
        if (-not [System.Windows.Forms.Clipboard]::ContainsImage()) {
            return
        }
        
        $image = [System.Windows.Forms.Clipboard]::GetImage()
        if ($null -eq $image) { return }
        
        # Create hash to detect changes
        $ms = New-Object System.IO.MemoryStream
        try {
            $image.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $imageBytes = $ms.ToArray()
            
            $md5 = [System.Security.Cryptography.MD5]::Create()
            $hashBytes = $md5.ComputeHash($imageBytes)
            $hash = [System.BitConverter]::ToString($hashBytes)
            $md5.Dispose()
            
            # Skip if same image
            if ($hash -eq $lastClipboardHash) {
                $image.Dispose()
                return
            }
            
            $script:lastClipboardHash = $hash
            
            # Play high pitch sound when image copied
            Play-Sound -frequency 1000 -duration 100
            
            # Save image to directory
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            $tempFileName = "clipboard-$timestamp.png"
            $tempFilePath = Join-Path $scriptDir $tempFileName
            
            # Ensure unique filename
            $counter = 1
            while (Test-Path $tempFilePath) {
                $tempFileName = "clipboard-$timestamp-$counter.png"
                $tempFilePath = Join-Path $scriptDir $tempFileName
                $counter++
            }
            
            $image.Save($tempFilePath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $ms.Dispose()
            if ($image) {
                $image.Dispose()
            }
        }
        
        # Process the saved image file
        Start-Sleep -Milliseconds 200
        Process-ImageFile -filePath $tempFilePath
    }
    catch {
        # Clipboard might be locked or contain non-image data
        # Silently continue
    }
}

# Function to start clipboard monitoring
function Start-ClipboardMonitoring {
    if ($clipboardTimer) {
        $clipboardTimer.Stop()
        $clipboardTimer.Dispose()
    }
    
    # Create timer to check clipboard with configurable interval
    $script:clipboardTimer = New-Object System.Windows.Forms.Timer
    $clipboardTimer.Interval = $clipboardCheckInterval
    $clipboardTimer.Add_Tick({ Check-Clipboard })
    $clipboardTimer.Start()
}

# FileSystemWatcher to monitor directory
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $scriptDir
$watcher.Filter = "*.*"
$watcher.IncludeSubdirectories = $false
$watcher.EnableRaisingEvents = $true

# Function to process image file
function Process-ImageFile {
    param (
        [string]$filePath
    )
    
    # Skip if paused
    if ($isPaused) { return }
    
    try {
        # Normalize path
        $filePath = [System.IO.Path]::GetFullPath($filePath)
        
        # Skip if already processed
        if ($processedFiles.ContainsKey($filePath)) { return }
        
        # Check if file is in the script directory (not in subdirectories)
        $fileDir = [System.IO.Path]::GetDirectoryName($filePath)
        if ($fileDir -ne $scriptDir) { return }
        
        $file = Get-Item $filePath -ErrorAction SilentlyContinue
        if (-not $file) { return }
        
        # Skip if file is locked or still being written
        try {
            $stream = [System.IO.File]::Open($filePath, 'Open', 'Read', 'None')
            $stream.Close()
        }
        catch {
            return
        }
        
        $extension = [System.IO.Path]::GetExtension($file.Name).ToLower()
        
        # Check if it's an image file
        if ($imageExtensions -contains $extension) {
            # Mark as processed
            $processedFiles[$filePath] = $true
            
            # Wait a bit to ensure file is fully written (configurable delay)
            Start-Sleep -Milliseconds $fileProcessingDelay
            
            # Get current date for folder name
            $dateFolder = Get-Date -Format "yyyy-MM-dd"
            $dateFolderPath = Join-Path $scriptDir $dateFolder
            
            # Create date folder if it doesn't exist
            if (-not (Test-Path $dateFolderPath)) {
                New-Item -ItemType Directory -Path $dateFolderPath -Force | Out-Null
            }
            
            # Count existing images in today's folder to get next number
            $existingImages = Get-ChildItem -Path $dateFolderPath -Filter "image-*" -File -ErrorAction SilentlyContinue
            $imageCount = ($existingImages.Count) + 1
            
            # Generate timestamp
            $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
            
            # Generate new filename
            $newFileName = "image-$timestamp-$imageCount$extension"
            $newFilePath = Join-Path $dateFolderPath $newFileName
            
            # Ensure unique filename
            $counter = 1
            while (Test-Path $newFilePath) {
                $newFileName = "image-$timestamp-$imageCount-$counter$extension"
                $newFilePath = Join-Path $dateFolderPath $newFileName
                $counter++
            }
            
            # Move and rename the file
            Move-Item -Path $filePath -Destination $newFilePath -Force -ErrorAction Stop
            $script:totalProcessed++
            # Write-Host "Moved: $($file.Name) -> $newFileName"  # Console hidden, no need for output
            
            # Play higher pitch sound when organized (only in clipboard mode)
            if ($mode -eq "Clipboard") {
                Play-Sound -frequency 1200 -duration 150
            }
            
            # Show notification
            if ($notifyImageProcessing) {
                $notifyIcon.BalloonTipText = "Organized: $newFileName"
                $notifyIcon.ShowBalloonTip(2000)
            }
        }
    }
    catch {
        # Write-Host "Error processing file: $_"  # Console hidden, no need for output
        # Remove from processed list on error so it can be retried
        if ($processedFiles.ContainsKey($filePath)) {
            $processedFiles.Remove($filePath)
        }
    }
}

# Event handler for file creation
$onCreated = Register-ObjectEvent $watcher "Created" -Action {
    $filePath = $Event.SourceEventArgs.FullPath
    Start-Sleep -Milliseconds 100
    Process-ImageFile -filePath $filePath
}

# Event handler for file rename (sometimes files are created then renamed)
$onRenamed = Register-ObjectEvent $watcher "Renamed" -Action {
    $filePath = $Event.SourceEventArgs.FullPath
    Start-Sleep -Milliseconds 100
    Process-ImageFile -filePath $filePath
}

# Initialize based on mode
if ($mode -eq "Clipboard") {
    $watcher.EnableRaisingEvents = $false
    if (-not $isPaused) {
        Start-ClipboardMonitoring
    }
}
else {
    $watcher.EnableRaisingEvents = -not $isPaused
}

# Initialize icon
Update-Icon

# Keep script running (no console output needed since window is hidden)
# Write-Host "Image Organizer started. Mode: $mode. Monitoring: $scriptDir"
# Write-Host "Press Ctrl+C to stop..."

try {
    # Application loop
    [System.Windows.Forms.Application]::Run()
}
finally {
    # Cleanup
    $watcher.EnableRaisingEvents = $false
    $watcher.Dispose()
    if ($clipboardTimer) {
        $clipboardTimer.Stop()
        $clipboardTimer.Dispose()
    }
    $notifyIcon.Visible = $false
    $notifyIcon.Dispose()
    Unregister-Event -SourceIdentifier $onCreated.Name -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier $onRenamed.Name -ErrorAction SilentlyContinue
}
