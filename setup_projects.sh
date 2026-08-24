#!/bin/bash
set -e

# Create directories
mkdir -p src/TaskTracker.Domain
mkdir -p src/TaskTracker.Application
mkdir -p src/TaskTracker.Infrastructure
mkdir -p src/TaskTracker.Windows
mkdir -p tests/TaskTracker.Domain.Tests
mkdir -p tests/TaskTracker.Application.Tests
mkdir -p tests/TaskTracker.Infrastructure.Tests
mkdir -p tests/TaskTracker.Windows.Tests
mkdir -p tests/fixtures
mkdir -p installer
mkdir -p .github/workflows

# Directory.Build.props
cat << 'EOF' > Directory.Build.props
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
EOF

# TaskTracker.Domain
cat << 'EOF' > src/TaskTracker.Domain/TaskTracker.Domain.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>TaskTracker.Domain</RootNamespace>
  </PropertyGroup>
</Project>
EOF

# TaskTracker.Application
cat << 'EOF' > src/TaskTracker.Application/TaskTracker.Application.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>TaskTracker.Application</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TaskTracker.Domain\TaskTracker.Domain.csproj" />
  </ItemGroup>
</Project>
EOF

# TaskTracker.Infrastructure
cat << 'EOF' > src/TaskTracker.Infrastructure/TaskTracker.Infrastructure.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>TaskTracker.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TaskTracker.Application\TaskTracker.Application.csproj" />
  </ItemGroup>
</Project>
EOF

# TaskTracker.Windows
cat << 'EOF' > src/TaskTracker.Windows/TaskTracker.Windows.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <RootNamespace>TaskTracker.Windows</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TaskTracker.Infrastructure\TaskTracker.Infrastructure.csproj" />
  </ItemGroup>
</Project>
EOF

# TaskTracker.Domain.Tests
cat << 'EOF' > tests/TaskTracker.Domain.Tests/TaskTracker.Domain.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.8.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.1">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TaskTracker.Domain\TaskTracker.Domain.csproj" />
  </ItemGroup>
</Project>
EOF

# TaskTracker.Application.Tests
cat << 'EOF' > tests/TaskTracker.Application.Tests/TaskTracker.Application.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.8.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.1">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TaskTracker.Application\TaskTracker.Application.csproj" />
  </ItemGroup>
</Project>
EOF

# TaskTracker.Infrastructure.Tests
cat << 'EOF' > tests/TaskTracker.Infrastructure.Tests/TaskTracker.Infrastructure.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.8.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.1">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TaskTracker.Infrastructure\TaskTracker.Infrastructure.csproj" />
  </ItemGroup>
</Project>
EOF

# TaskTracker.Windows.Tests
cat << 'EOF' > tests/TaskTracker.Windows.Tests/TaskTracker.Windows.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.8.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.1">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TaskTracker.Windows\TaskTracker.Windows.csproj" />
  </ItemGroup>
</Project>
EOF

# Solution file
cat << 'EOF' > TaskTracker.sln
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Domain", "src\TaskTracker.Domain\TaskTracker.Domain.csproj", "{D1C68E2F-65B6-4E87-A173-F4BE21226BB3}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Application", "src\TaskTracker.Application\TaskTracker.Application.csproj", "{F24B5C6C-9D7A-4B6A-B68C-D52A2D9D9994}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Infrastructure", "src\TaskTracker.Infrastructure\TaskTracker.Infrastructure.csproj", "{4E2F7F2D-6D25-45B0-9A21-7A3982C9EAE7}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Windows", "src\TaskTracker.Windows\TaskTracker.Windows.csproj", "{9AC2A2B2-9694-4F93-8751-24EE4F6B9B3C}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Domain.Tests", "tests\TaskTracker.Domain.Tests\TaskTracker.Domain.Tests.csproj", "{1D0C4A19-9C5F-4E38-B549-F6A7B2C5B3E1}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Application.Tests", "tests\TaskTracker.Application.Tests\TaskTracker.Application.Tests.csproj", "{8F6A3D1E-6C7A-4D9A-B561-D5A8C9B9C7F2}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Infrastructure.Tests", "tests\TaskTracker.Infrastructure.Tests\TaskTracker.Infrastructure.Tests.csproj", "{E8D7B5C3-7F1E-4E9A-B4C2-F5D4B6A2C1F0}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TaskTracker.Windows.Tests", "tests\TaskTracker.Windows.Tests\TaskTracker.Windows.Tests.csproj", "{B8C7E1F2-9A4B-4D3C-8E5F-C6D7F4B3A2E1}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{D1C68E2F-65B6-4E87-A173-F4BE21226BB3}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{D1C68E2F-65B6-4E87-A173-F4BE21226BB3}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{D1C68E2F-65B6-4E87-A173-F4BE21226BB3}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{D1C68E2F-65B6-4E87-A173-F4BE21226BB3}.Release|Any CPU.Build.0 = Release|Any CPU
		{F24B5C6C-9D7A-4B6A-B68C-D52A2D9D9994}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{F24B5C6C-9D7A-4B6A-B68C-D52A2D9D9994}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{F24B5C6C-9D7A-4B6A-B68C-D52A2D9D9994}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{F24B5C6C-9D7A-4B6A-B68C-D52A2D9D9994}.Release|Any CPU.Build.0 = Release|Any CPU
		{4E2F7F2D-6D25-45B0-9A21-7A3982C9EAE7}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{4E2F7F2D-6D25-45B0-9A21-7A3982C9EAE7}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{4E2F7F2D-6D25-45B0-9A21-7A3982C9EAE7}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{4E2F7F2D-6D25-45B0-9A21-7A3982C9EAE7}.Release|Any CPU.Build.0 = Release|Any CPU
		{9AC2A2B2-9694-4F93-8751-24EE4F6B9B3C}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{9AC2A2B2-9694-4F93-8751-24EE4F6B9B3C}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{9AC2A2B2-9694-4F93-8751-24EE4F6B9B3C}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{9AC2A2B2-9694-4F93-8751-24EE4F6B9B3C}.Release|Any CPU.Build.0 = Release|Any CPU
		{1D0C4A19-9C5F-4E38-B549-F6A7B2C5B3E1}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{1D0C4A19-9C5F-4E38-B549-F6A7B2C5B3E1}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{1D0C4A19-9C5F-4E38-B549-F6A7B2C5B3E1}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{1D0C4A19-9C5F-4E38-B549-F6A7B2C5B3E1}.Release|Any CPU.Build.0 = Release|Any CPU
		{8F6A3D1E-6C7A-4D9A-B561-D5A8C9B9C7F2}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{8F6A3D1E-6C7A-4D9A-B561-D5A8C9B9C7F2}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{8F6A3D1E-6C7A-4D9A-B561-D5A8C9B9C7F2}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{8F6A3D1E-6C7A-4D9A-B561-D5A8C9B9C7F2}.Release|Any CPU.Build.0 = Release|Any CPU
		{E8D7B5C3-7F1E-4E9A-B4C2-F5D4B6A2C1F0}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{E8D7B5C3-7F1E-4E9A-B4C2-F5D4B6A2C1F0}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{E8D7B5C3-7F1E-4E9A-B4C2-F5D4B6A2C1F0}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{E8D7B5C3-7F1E-4E9A-B4C2-F5D4B6A2C1F0}.Release|Any CPU.Build.0 = Release|Any CPU
		{B8C7E1F2-9A4B-4D3C-8E5F-C6D7F4B3A2E1}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{B8C7E1F2-9A4B-4D3C-8E5F-C6D7F4B3A2E1}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{B8C7E1F2-9A4B-4D3C-8E5F-C6D7F4B3A2E1}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{B8C7E1F2-9A4B-4D3C-8E5F-C6D7F4B3A2E1}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
EOF
