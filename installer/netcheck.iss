#define DotNetDesktopRuntimeUrl "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.23/windowsdesktop-runtime-8.0.23-win-x64.exe"
#define DotNetDesktopRuntimeFile "windowsdesktop-runtime-8.0.23-win-x64.exe"
#define DotNetDesktopRuntimeMin "8.0.0"

[Setup]
AppId={{E5F834F0-5B62-4E6B-9F1E-7F9E2B1F1C32}
AppName=Netcheck
AppVersion=0.1.6
AppPublisher=Polylabs
DefaultDirName={pf}\Netcheck
DefaultGroupName=Netcheck
UninstallDisplayIcon={app}\Netcheck.exe
OutputBaseFilename=Netcheck-Setup-0.1.6
OutputDir=.\out
SetupIconFile=..\Assets\netcheck.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "..\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Excludes: "*.pdb,Netcheck.exe.WebView2\\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Netcheck"; Filename: "{app}\Netcheck.exe"
Name: "{commondesktop}\Netcheck"; Filename: "{app}\Netcheck.exe"; Tasks: desktopicon
Name: "{group}\Desinstaller Netcheck"; Filename: "{uninstallexe}"

[Tasks]
Name: "desktopicon"; Description: "Creer une icone sur le Bureau"; GroupDescription: "Icones supplementaires:"

[Code]
const
  DotNetDesktopRuntimeUrl = '{#DotNetDesktopRuntimeUrl}';
  DotNetDesktopRuntimeFile = '{#DotNetDesktopRuntimeFile}';
  DotNetDesktopRuntimeMin = '{#DotNetDesktopRuntimeMin}';

const
  INTERNET_OPEN_TYPE_PRECONFIG = 0;
  INTERNET_FLAG_RELOAD = $80000000;

type
  HINTERNET = LongWord;

function InternetOpen(lpszAgent: string; dwAccessType: Integer; lpszProxy, lpszProxyBypass: string; dwFlags: Integer): HINTERNET;
  external 'InternetOpenW@wininet.dll stdcall';
function InternetOpenUrl(hInternet: HINTERNET; lpszUrl: string; lpszHeaders: string; dwHeadersLength: Integer; dwFlags: Integer; dwContext: Integer): HINTERNET;
  external 'InternetOpenUrlW@wininet.dll stdcall';
function InternetReadFile(hFile: HINTERNET; lpBuffer: string; dwNumberOfBytesToRead: Integer; var lpdwNumberOfBytesRead: Integer): Boolean;
  external 'InternetReadFile@wininet.dll stdcall';
function InternetCloseHandle(hInet: HINTERNET): Boolean;
  external 'InternetCloseHandle@wininet.dll stdcall';
function HttpQueryInfo(hRequest: HINTERNET; dwInfoLevel: Integer; lpBuffer: string; var lpdwBufferLength: Integer; var lpdwIndex: Integer): Boolean;
  external 'HttpQueryInfoW@wininet.dll stdcall';

function GetVersionPart(const S: string; Index: Integer): Integer;
var
  I: Integer;
  PartIndex: Integer;
  Part: string;
  Ch: Char;
begin
  Part := '';
  PartIndex := 0;

  for I := 1 to Length(S) do
  begin
    Ch := S[I];
    if Ch = '.' then
    begin
      if PartIndex = Index then
      begin
        Result := StrToIntDef(Part, 0);
        Exit;
      end;
      Part := '';
      PartIndex := PartIndex + 1;
    end
    else
      Part := Part + Ch;
  end;

  if PartIndex = Index then
    Result := StrToIntDef(Part, 0)
  else
    Result := 0;
end;

function VersionGreaterOrEqual(const A, B: string): Boolean;
var
  A0, A1, A2, B0, B1, B2: Integer;
begin
  A0 := GetVersionPart(A, 0);
  A1 := GetVersionPart(A, 1);
  A2 := GetVersionPart(A, 2);
  B0 := GetVersionPart(B, 0);
  B1 := GetVersionPart(B, 1);
  B2 := GetVersionPart(B, 2);

  if A0 <> B0 then
    Result := A0 > B0
  else if A1 <> B1 then
    Result := A1 > B1
  else
    Result := A2 >= B2;
end;

function IsDesktopRuntimeInstalled: Boolean;
var
  ValueNames: TArrayOfString;
  I: Integer;
  Version: string;
begin
  Result := False;

  if RegGetValueNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', ValueNames) then
  begin
    for I := 0 to GetArrayLength(ValueNames) - 1 do
    begin
      Version := ValueNames[I];
      if VersionGreaterOrEqual(Version, DotNetDesktopRuntimeMin) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  if RegGetValueNames(HKLM, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', ValueNames) then
  begin
    for I := 0 to GetArrayLength(ValueNames) - 1 do
    begin
      Version := ValueNames[I];
      if VersionGreaterOrEqual(Version, DotNetDesktopRuntimeMin) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function DownloadFile(const URL, DestFile: string): Boolean;
var
  hInet: HINTERNET;
  hUrl: HINTERNET;
  Buffer: string;
  BytesRead: Integer;
  FileStream: TFileStream;
  TotalRead: Integer;
  ContentLenStr: string;
  ContentLen: Integer;
  Len: Integer;
  Index: Integer;
  ProgressText: string;
  ReadMbText: string;
  TotalMbText: string;
  ReadMb10: Integer;
  TotalMb10: Integer;
begin
  Result := False;
  TotalRead := 0;
  ContentLen := 0;
  BytesRead := 0;

  hInet := InternetOpen('NetcheckSetup', INTERNET_OPEN_TYPE_PRECONFIG, '', '', 0);
  if hInet = 0 then Exit;
  hUrl := InternetOpenUrl(hInet, URL, '', 0, INTERNET_FLAG_RELOAD, 0);
  if hUrl = 0 then
  begin
    InternetCloseHandle(hInet);
    Exit;
  end;

  Len := 1024;
  ContentLenStr := StringOfChar(#0, Len);
  Index := 0;
  if HttpQueryInfo(hUrl, $20000005 {HTTP_QUERY_CONTENT_LENGTH}, ContentLenStr, Len, Index) then
  begin
    ContentLenStr := Trim(Copy(ContentLenStr, 1, Len));
    ContentLen := StrToIntDef(ContentLenStr, 0);
  end;

  WizardForm.ProgressGauge.Min := 0;
  WizardForm.ProgressGauge.Max := 100;
  WizardForm.ProgressGauge.Position := 0;
  WizardForm.StatusLabel.Caption := 'Telechargement du .NET Desktop Runtime...';

  FileStream := TFileStream.Create(DestFile, fmCreate);
  try
    Buffer := StringOfChar(#0, 65536);
    repeat
      if not InternetReadFile(hUrl, Buffer, Length(Buffer), BytesRead) then
        Break;
      if BytesRead = 0 then
        Break;
      FileStream.WriteBuffer(Buffer[1], BytesRead);
      TotalRead := TotalRead + BytesRead;
      if ContentLen > 0 then
      begin
        WizardForm.ProgressGauge.Position := Trunc((TotalRead * 100) / ContentLen);
        ReadMb10 := (TotalRead * 10) div 1048576;
        TotalMb10 := (ContentLen * 10) div 1048576;
        ReadMbText := IntToStr(ReadMb10 div 10) + ',' + IntToStr(ReadMb10 mod 10);
        TotalMbText := IntToStr(TotalMb10 div 10) + ',' + IntToStr(TotalMb10 mod 10);
        ProgressText := 'Telechargement du .NET Desktop Runtime... ' + ReadMbText + ' Mo / ' + TotalMbText + ' Mo';
        WizardForm.StatusLabel.Caption := ProgressText;
      end
      else
        WizardForm.StatusLabel.Caption := 'Telechargement du .NET Desktop Runtime...';
    until False;
  finally
    FileStream.Free;
  end;

  InternetCloseHandle(hUrl);
  InternetCloseHandle(hInet);

  Result := FileExists(DestFile);
  WizardForm.ProgressGauge.Position := WizardForm.ProgressGauge.Max;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  TmpFile: string;
  ResultCode: Integer;
begin
  Result := '';

  if not IsDesktopRuntimeInstalled then
  begin
    MsgBox('Le .NET Desktop Runtime 8 est requis. L''installateur va le telecharger.', mbInformation, MB_OK);
    TmpFile := ExpandConstant('{tmp}\') + DotNetDesktopRuntimeFile;

    if not DownloadFile(DotNetDesktopRuntimeUrl, TmpFile) then
    begin
      Result := 'Echec du telechargement du .NET Desktop Runtime.';
      Exit;
    end;

    if not Exec(TmpFile, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := 'Echec du lancement de l''installateur .NET Desktop Runtime.';
      Exit;
    end;

    if ResultCode <> 0 then
    begin
      Result := 'Installation du .NET Desktop Runtime a echoue (code ' + IntToStr(ResultCode) + ').';
      Exit;
    end;
  end;
end;
