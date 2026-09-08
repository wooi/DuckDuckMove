param([string]$Version = '0.1.1')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $root 'dist/win-x64'
$work = Join-Path $root 'artifacts/msi-build'
$output = Join-Path $root "dist/DuckDuckMove-$Version-x64.msi"
New-Item -ItemType Directory -Path $work -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $payload 'DuckDuckMove.exe'))) { throw 'Publish the app first.' }

# Pure Windows SDK interfaces: no downloaded compiler or executable installer custom actions.
$installer = New-Object -ComObject WindowsInstaller.Installer
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
$db = $installer.OpenDatabase($output, 3)
function Execute-Sql([string]$sql) {
    $view = $db.OpenView($sql)
    try { $view.Execute() } finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view) }
}
function Add-Row([string]$table, [string[]]$columns, [object[]]$values) {
    $names = ($columns | ForEach-Object { '`' + $_ + '`' }) -join ','
    $markers = ($columns | ForEach-Object { '?' }) -join ','
    $view = $db.OpenView('INSERT INTO `' + $table + '` (' + $names + ') VALUES (' + $markers + ')')
    $record = $installer.CreateRecord($values.Count)
    try {
        for ($i = 0; $i -lt $values.Count; $i++) {
            if ($null -eq $values[$i]) { continue }
            $property = if ($values[$i] -is [int]) { 'IntegerData' } else { 'StringData' }
            $fieldValue = if ($property -eq 'IntegerData') { [int]$values[$i] } else { [string]$values[$i] }
            [void]$record.GetType().InvokeMember($property, 'SetProperty', $null, $record, @(($i + 1), $fieldValue))
        }
        $view.Execute($record)
    } catch { throw "Table $table / $($values[0]) field $i ($($columns[[Math]::Min($i,$columns.Count-1)])): $_" }
    finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($record) }
}
function Add-Stream([string]$table, [string]$keyColumn, [string]$dataColumn, [string]$name, [string]$path) {
    $view = $db.OpenView('INSERT INTO `' + $table + '` (`' + $keyColumn + '`,`' + $dataColumn + '`) VALUES (?,?)')
    $record = $installer.CreateRecord(2)
    try {
        [void]$record.GetType().InvokeMember('StringData', 'SetProperty', $null, $record, @(1, $name))
        $record.SetStream(2, $path); $view.Execute($record)
    } finally { $view.Close(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($view); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($record) }
}
function Stable-Guid([string]$name) {
    $bytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('DuckDuckMove/' + $name))
    return ([guid]::new([byte[]]$bytes[0..15])).ToString('B').ToUpperInvariant()
}

[IO.File]::WriteAllText((Join-Path $work 'codepage.idt'), "`r`n`r`n936`t_ForceCodepage`r`n", [Text.Encoding]::ASCII)
$db.Import($work, 'codepage.idt')
$definitions = @(
 'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)',
 'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)',
 'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)',
 'CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)',
 'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)',
 'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)',
 'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)',
 'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)',
 'CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)',
 'CREATE TABLE `Icon` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)',
 'CREATE TABLE `Binary` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)',
 'CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)',
 'CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)',
 'CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)',
 'CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)',
 'CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)',
 'CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50) NOT NULL, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)',
 'CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(72), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)',
 'CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)',
 'CREATE TABLE `CheckBox` (`Property` CHAR(72) NOT NULL, `Value` CHAR(64) PRIMARY KEY `Property`)',
 'CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)',
 'CREATE TABLE `EventMapping` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Attribute` CHAR(50) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Event`)',
 'CREATE TABLE `ActionText` (`Action` CHAR(72) NOT NULL, `Description` CHAR(64) LOCALIZABLE, `Template` CHAR(128) LOCALIZABLE PRIMARY KEY `Action`)'
)
foreach ($definition in $definitions) { Execute-Sql $definition }
$productCode = Stable-Guid "Product/$Version"
$upgradeCode = '{77A46B04-813B-4AC0-BB66-998A4BBD07B7}'
$properties = [ordered]@{
 ProductName='DuckDuckMove'; ProductVersion=$Version; ProductCode=$productCode; UpgradeCode=$upgradeCode;
 Manufacturer='DuckDuckMove'; ProductLanguage='2052'; INSTALLLEVEL='1'; ARPNOMODIFY='1'; ARPNOREPAIR='1';
 ARPPRODUCTICON='DuckIcon'; ARPCOMMENTS='Windows 11 动图头像工具（测试版）';
 DefaultUIFont='Normal'; DISABLEADVTSHORTCUTS='1'; SecureCustomProperties='OLDPRODUCTS;NEWERPRODUCTS;DESKTOPSHORTCUT';
 MSIRESTARTMANAGERCONTROL='Disable'; REBOOT='ReallySuppress'
}
foreach ($entry in $properties.GetEnumerator()) { Add-Row 'Property' @('Property','Value') @($entry.Key,$entry.Value) }
foreach ($row in @(
 @('TARGETDIR',$null,'SourceDir'), @('LocalAppDataFolder','TARGETDIR','.'), @('ProgramsDir','LocalAppDataFolder','Programs'),
 @('INSTALLDIR','ProgramsDir','DuckDuckMove'), @('ProgramMenuFolder','TARGETDIR','.'), @('MenuDir','ProgramMenuFolder','DuckDuckMove'),
 @('DesktopFolder','TARGETDIR','.'), @('NoticesDir','INSTALLDIR','third-party'), @('SamplesDir','INSTALLDIR','samples')
)) { Add-Row 'Directory' @('Directory','Directory_Parent','DefaultDir') $row }
Add-Row 'Feature' @('Feature','Title','Description','Display','Level','Directory_','Attributes') @('Main','DuckDuckMove','程序、内置小鸭和使用说明',1,1,'INSTALLDIR',0)
Add-Row 'Feature' @('Feature','Feature_Parent','Title','Display','Level','Directory_','Attributes') @('Desktop','Main','桌面快捷方式',0,1,'DesktopFolder',0)
Add-Row 'LaunchCondition' @('Condition','Description') @('Installed OR (VersionNT64 AND Msix64)','DuckDuckMove 目前仅支持 Windows 11 x64。')
# Windows Installer reports VersionNT=603 on modern Windows; read the actual build through AppSearch.
Execute-Sql 'CREATE TABLE `AppSearch` (`Property` CHAR(72) NOT NULL, `Signature_` CHAR(72) NOT NULL PRIMARY KEY `Property`, `Signature_`)'
Execute-Sql 'CREATE TABLE `Signature` (`Signature` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `MinVersion` CHAR(20), `MaxVersion` CHAR(20), `MinSize` LONG, `MaxSize` LONG, `MinDate` LONG, `MaxDate` LONG, `Languages` CHAR(255) PRIMARY KEY `Signature`)'
Execute-Sql 'CREATE TABLE `RegLocator` (`Signature_` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL, `Name` CHAR(255), `Type` SHORT PRIMARY KEY `Signature_`)'
Add-Row 'AppSearch' @('Property','Signature_') @('WINDOWSBUILD','BuildProbe')
Add-Row 'AppSearch' @('Property','Signature_') @('INSTALLDIR','InstallPathProbe')
Add-Row 'RegLocator' @('Signature_','Root','Key','Name','Type') @('InstallPathProbe',1,'Software\DuckDuckMove\Installer','InstallPath',18)
Add-Row 'RegLocator' @('Signature_','Root','Key','Name','Type') @('BuildProbe',2,'SOFTWARE\Microsoft\Windows NT\CurrentVersion','CurrentBuildNumber',18)
Add-Row 'LaunchCondition' @('Condition','Description') @('Installed OR WINDOWSBUILD >= "22000"','此安装包需要 Windows 11（版本号 22000 或更高）。')
Add-Row 'LaunchCondition' @('Condition','Description') @('NOT NEWERPRODUCTS','已安装更高版本，请使用已安装的 DuckDuckMove。')
Add-Row 'Upgrade' @('UpgradeCode','VersionMin','VersionMax','Attributes','ActionProperty') @($upgradeCode,'0.0.0',$Version,256,'OLDPRODUCTS')
Add-Row 'Upgrade' @('UpgradeCode','VersionMin','Attributes','ActionProperty') @($upgradeCode,$Version,2,'NEWERPRODUCTS')

$files = [Collections.Generic.List[object]]::new()
$files.Add(@('DuckDuckMove.exe','INSTALLDIR','AppFile'))
$files.Add(@('README.md','INSTALLDIR','ReadmeFile'))
$files.Add(@('LICENSE','INSTALLDIR','LicenseFile'))
Add-Row 'Directory' @('Directory','Directory_Parent','DefaultDir') @('DocsDir','INSTALLDIR','docs')
Add-Row 'Directory' @('Directory','Directory_Parent','DefaultDir') @('ImagesDir','DocsDir','images')
foreach ($doc in Get-ChildItem -LiteralPath (Join-Path $payload 'docs') -File | Sort-Object Name) {
    $files.Add(@(('docs/'+$doc.Name),'DocsDir',('Doc'+$files.Count)))
}
foreach ($shot in Get-ChildItem -LiteralPath (Join-Path $payload 'docs/images') -File | Sort-Object Name) {
    $files.Add(@(('docs/images/'+$shot.Name),'ImagesDir',('Shot'+$files.Count)))
}
$files.Add(@('samples/duckduckmove-duck.gif','SamplesDir','SampleDuck'))
foreach ($notice in Get-ChildItem -LiteralPath (Join-Path $payload 'third-party') -File | Sort-Object Name) {
    $files.Add(@(('third-party/'+$notice.Name),'NoticesDir',('Notice'+$files.Count)))
}
$ddf = [Collections.Generic.List[string]]::new()
$ddf.Add('.OPTION EXPLICIT'); $ddf.Add('.Set CabinetNameTemplate=payload.cab'); $ddf.Add('.Set DiskDirectoryTemplate="'+$work+'"')
$ddf.Add('.Set CompressionType=MSZIP'); $ddf.Add('.Set Cabinet=ON'); $ddf.Add('.Set Compress=ON'); $ddf.Add('.Set MaxDiskSize=0'); $ddf.Add('.Set MaxCabinetSize=0')
$sequence=0
foreach ($item in $files) {
    $sequence++; $source=Join-Path $payload $item[0]; $id=$item[2]; $component='C'+$id; $filename=Split-Path -Leaf $source
    Add-Row 'Component' @('Component','ComponentId','Directory_','Attributes','KeyPath') @($component,(Stable-Guid ('Component/'+$item[0])),$item[1],260,('R'+$id))
    Add-Row 'Registry' @('Registry','Root','Key','Name','Value','Component_') @(('R'+$id),1,'Software\DuckDuckMove\Installer',$id,'[ProductVersion]',$component)
    $fileVersion=if($id -eq 'AppFile'){$Version+'.0'}else{$null}
    Add-Row 'File' @('File','Component_','FileName','FileSize','Version','Attributes','Sequence') @($id,$component,$filename,[int](Get-Item -LiteralPath $source).Length,$fileVersion,16384,$sequence)
    Add-Row 'FeatureComponents' @('Feature_','Component_') @('Main',$component)
    $ddf.Add('"'+$source+'" '+$id)
}
Add-Row 'Component' @('Component','ComponentId','Directory_','Attributes','Condition','KeyPath') @('CDesktop',(Stable-Guid 'Component/Desktop'),'DesktopFolder',260,'DESKTOPSHORTCUT=1','RDesktop')
Add-Row 'Registry' @('Registry','Root','Key','Name','Value','Component_') @('RInstallPath',1,'Software\DuckDuckMove\Installer','InstallPath','[INSTALLDIR]','CAppFile')
Add-Row 'Registry' @('Registry','Root','Key','Name','Value','Component_') @('RDesktop',1,'Software\DuckDuckMove\Installer','Desktop','1','CDesktop')
Add-Row 'FeatureComponents' @('Feature_','Component_') @('Desktop','CDesktop')
foreach ($row in @(
 @('MenuApp','MenuDir','DuckDuckMove','CAppFile','[#AppFile]','DuckIcon','INSTALLDIR'),
 @('DesktopApp','DesktopFolder','DuckDuckMove','CDesktop','[#AppFile]','DuckIcon','INSTALLDIR')
)) { Add-Row 'Shortcut' @('Shortcut','Directory_','Name','Component_','Target','Icon_','WkDir') $row }
Add-Row 'RemoveFile' @('FileKey','Component_','DirProperty','InstallMode') @('RemoveMenu','CAppFile','MenuDir',2)
foreach($directory in @('ImagesDir','DocsDir','SamplesDir','NoticesDir','INSTALLDIR')) { Add-Row 'RemoveFile' @('FileKey','Component_','DirProperty','InstallMode') @(('Remove'+$directory),'CAppFile',$directory,2) }
Add-Stream 'Icon' 'Name' 'Data' 'DuckIcon' (Join-Path $root 'src/DuckDuckMove.App/app.ico')
Add-Stream 'Binary' 'Name' 'Data' 'DuckLogo' (Join-Path $root 'src/DuckDuckMove.App/app.ico')
$ddfPath=Join-Path $work 'payload.ddf'
[IO.File]::WriteAllLines($ddfPath,$ddf,[Text.Encoding]::Default)
& "$env:WINDIR/system32/makecab.exe" /F $ddfPath | Out-Null
if($LASTEXITCODE -ne 0){throw 'makecab failed'}
Add-Row 'Media' @('DiskId','LastSequence','Cabinet') @(1,$sequence,'#payload.cab')
Add-Stream '_Streams' 'Name' 'Data' 'payload.cab' (Join-Path $work 'payload.cab')

# A compact Chinese wizard, a maintenance/uninstall page, and a progress dialog.
Add-Row 'TextStyle' @('TextStyle','FaceName','Size','Color','StyleBits') @('Normal','Microsoft YaHei UI',9,0,0)
Add-Row 'TextStyle' @('TextStyle','FaceName','Size','Color','StyleBits') @('Heading','Microsoft YaHei UI',16,0,1)
Add-Row 'CheckBox' @('Property','Value') @('DESKTOPSHORTCUT','1')
function Add-Control([string]$dialog,[string]$name,[string]$type,[int]$x,[int]$y,[int]$w,[int]$h,[string]$text,[object]$property=$null,[object]$next=$null,[int]$attributes=3) {
    Add-Row 'Control' @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Property','Text','Control_Next') @($dialog,$name,$type,$x,$y,$w,$h,$attributes,$property,$text,$next)
}
function Add-Event([string]$dialog,[string]$control,[string]$event,[string]$argument,[int]$order=1) {
    Add-Row 'ControlEvent' @('Dialog_','Control_','Event','Argument','Condition','Ordering') @($dialog,$control,$event,$argument,'1',$order)
}
Add-Row 'Dialog' @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @('Welcome',50,50,390,275,3,'DuckDuckMove 安装','Install','Install','Cancel')
Add-Control 'Welcome' 'Logo' 'Icon' 22 18 42 42 'DuckLogo'
Add-Control 'Welcome' 'Heading' 'Text' 76 19 285 27 '{\Heading}让头像，动起来。'
Add-Control 'Welcome' 'Intro' 'Text' 76 50 285 34 '安装 DuckDuckMove [ProductVersion]。内置小鸭动图，可直接预览和应用。'
Add-Control 'Welcome' 'FolderLabel' 'Text' 24 99 342 17 '安装到当前用户目录：'
Add-Control 'Welcome' 'Folder' 'Text' 24 119 342 37 '[INSTALLDIR]'
Add-Control 'Welcome' 'Desktop' 'CheckBox' 24 166 340 20 '创建桌面快捷方式' 'DESKTOPSHORTCUT' 'Install'
Add-Control 'Welcome' 'Note' 'Text' 24 194 342 30 '设置头像时需要管理员授权。卸载保留头像素材和备份。'
Add-Control 'Welcome' 'Line' 'Line' 0 235 390 0 ''
Add-Control 'Welcome' 'Install' 'PushButton' 217 245 72 20 '安装' $null 'Cancel'
Add-Control 'Welcome' 'Cancel' 'PushButton' 299 245 67 20 '取消' $null 'Desktop'
Add-Event 'Welcome' 'Install' 'EndDialog' 'Return'
Add-Event 'Welcome' 'Cancel' 'EndDialog' 'Exit'

Add-Row 'Dialog' @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @('Maintenance',50,50,390,240,3,'DuckDuckMove','Cancel','Cancel','Cancel')
Add-Control 'Maintenance' 'Heading' 'Text' 24 25 342 30 '{\Heading}DuckDuckMove 已安装'
Add-Control 'Maintenance' 'Info' 'Text' 24 80 342 72 '可从开始菜单打开软件，或点击下方按钮卸载。卸载会保留已设置的头像和原头像备份；如需还原，请先在软件中恢复。'
Add-Control 'Maintenance' 'Remove' 'PushButton' 217 198 72 22 '卸载' $null 'Cancel'
Add-Control 'Maintenance' 'Cancel' 'PushButton' 299 198 67 22 '关闭' $null 'Remove'
Add-Event 'Maintenance' 'Remove' '[REMOVE]' 'ALL' 1
Add-Event 'Maintenance' 'Remove' 'EndDialog' 'Return' 2
Add-Event 'Maintenance' 'Cancel' 'EndDialog' 'Exit'

Add-Row 'Dialog' @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @('Progress',50,50,390,200,1,'DuckDuckMove','Cancel','Cancel','Cancel')
Add-Control 'Progress' 'Heading' 'Text' 24 24 342 30 '{\Heading}正在处理，请稍候'
Add-Control 'Progress' 'Status' 'Text' 24 80 342 24 '正在准备文件…'
Add-Control 'Progress' 'Bar' 'ProgressBar' 24 112 342 14 ''
Add-Control 'Progress' 'Cancel' 'PushButton' 299 164 67 22 '取消'
Add-Event 'Progress' 'Cancel' 'EndDialog' 'Exit'
Add-Row 'EventMapping' @('Dialog_','Control_','Event','Attribute') @('Progress','Bar','SetProgress','Progress')
Add-Row 'EventMapping' @('Dialog_','Control_','Event','Attribute') @('Progress','Status','ActionText','Text')
foreach($exitPage in @(@('Complete','已完成','请求的操作已完成。安装后可从开始菜单打开软件；卸载后仍会保留头像素材及备份。'),@('Failure','未能完成','安装遇到问题，未完成的操作将回滚。可重新运行安装包重试。'),@('Cancelled','已取消','操作已取消。'))){
    $name=$exitPage[0]
    Add-Row 'Dialog' @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @($name,50,50,390,220,3,'DuckDuckMove','Finish','Finish','Finish')
    Add-Control $name 'Heading' 'Text' 24 25 342 30 ('{\Heading}'+$exitPage[1])
    Add-Control $name 'Info' 'Text' 24 82 342 70 $exitPage[2]
    Add-Control $name 'Finish' 'PushButton' 294 181 72 22 '完成'
    Add-Event $name 'Finish' 'EndDialog' 'Return'
}

$executeSequence=@(
 @('FindRelatedProducts',25),@('AppSearch',50),@('LaunchConditions',100),@('ValidateProductID',700),
 @('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('MigrateFeatureStates',1200),
 @('InstallValidate',1400),@('RemoveExistingProducts',1450),@('InstallInitialize',1500),@('ProcessComponents',1600),
 @('UnpublishFeatures',1800),@('RemoveShortcuts',3200),@('RemoveRegistryValues',3300),@('RemoveFiles',3500),
 @('InstallFiles',4000),@('CreateShortcuts',4500),@('WriteRegistryValues',5000),@('RegisterUser',6000),
 @('RegisterProduct',6100),@('PublishFeatures',6300),@('PublishProduct',6400),@('InstallFinalize',6600)
)
foreach($action in $executeSequence){Add-Row 'InstallExecuteSequence' @('Action','Sequence') $action}
foreach($action in @(@('FindRelatedProducts',25),@('AppSearch',50),@('LaunchConditions',100),@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000))){Add-Row 'InstallUISequence' @('Action','Sequence') $action}
foreach($row in @(@('Welcome','NOT Installed',1100),@('Maintenance','Installed AND NOT REMOVE',1110),@('Progress','1',1200),@('ExecuteAction','1',1300),@('Complete','1',-1),@('Cancelled','1',-2),@('Failure','1',-3))){Add-Row 'InstallUISequence' @('Action','Condition','Sequence') $row}
foreach($row in @(@('InstallFiles','正在安装程序文件'),@('CreateShortcuts','正在创建快捷方式'),@('WriteRegistryValues','正在注册应用'),@('RemoveFiles','正在移除程序文件'),@('RemoveShortcuts','正在移除快捷方式'))){Add-Row 'ActionText' @('Action','Description') $row}
$summary=$db.SummaryInformation(20)
foreach($entry in @(@(1,936),@(2,'DuckDuckMove 安装包'),@(3,'Windows 11 动图头像工具'),@(4,'DuckDuckMove'),@(7,'x64;2052'),@(9,([guid]::NewGuid().ToString('B').ToUpperInvariant())),@(14,500),@(15,10),@(18,'DuckDuckMove native MSI builder'),@(19,2))){
    [void]$summary.GetType().InvokeMember('Property','SetProperty',$null,$summary,@($entry[0],$entry[1]))
}
$summary.Persist(); $db.Commit()
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($summary)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
[pscustomobject]@{Installer=$output;ProductCode=$productCode;Bytes=(Get-Item -LiteralPath $output).Length} | Format-List
