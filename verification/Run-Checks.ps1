param([switch]$CursorOnly, [switch]$NativeOnly, [switch]$EditorOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$runnerDir = Join-Path $repoRoot 'obj\verification'
New-Item -ItemType Directory -Force -Path $runnerDir | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <StartupObject>Verification.Checks</StartupObject><RootNamespace>MDviewer</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <AssemblyMetadata Include="BuildDate" Value="verification" />
    <EmbeddedResource Include="../../assets/*" />
    <EmbeddedResource Include="../../test-sample.md" LogicalName="MDviewer.test-sample.md" />
    <EmbeddedResource Include="../../diagram-sample.md" LogicalName="MDviewer.diagram-sample.md" />
    <Compile Include="../../Program.cs" />
    <Compile Include="../../WordExport.cs" />
    <Compile Include="../../verification/Checks.cs.txt" />
    <PackageReference Include="Markdig" Version="0.41.3" />
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.3351.48" />
    <PackageReference Include="DocumentFormat.OpenXml" Version="3.3.0" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $runnerDir 'Verification.csproj') -Encoding UTF8
$runnerArgs = @($repoRoot)
if ($CursorOnly) { $runnerArgs += '--cursor' }
if ($NativeOnly) { $runnerArgs += '--native' }
if ($EditorOnly) { $runnerArgs += '--editor' }
dotnet run --project (Join-Path $runnerDir 'Verification.csproj') -- @runnerArgs
exit $LASTEXITCODE
