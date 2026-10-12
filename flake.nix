{
  description = "Mi Band Heartrate - cross-platform Mi Band heart rate monitor with VRChat OSC output";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { inherit system; };

        # Runtime libraries the Avalonia GUI needs on Linux:
        # Skia/HarfBuzz (rendering, fontconfig/freetype), X11 + session
        # management libs (windowing) and ICU (dotnet globalization).
        guiRuntimeDeps = with pkgs; [
          fontconfig
          freetype
          libX11
          libICE
          libSM
          libXext
          libXcursor
          libXrandr
          libXi
          libxkbcommon
          icu
        ];

        dotnet = pkgs.dotnet-sdk_10;
      in
      {
        # ----------------------------------------------------------------
        # Development shell: `nix develop`
        # Provides the .NET SDK and the native libraries required to build
        # and run the Avalonia GUI (and the CLI) from source.
        # ----------------------------------------------------------------
        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet
            fontconfig
            freetype
          ];

          LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath guiRuntimeDeps;

          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
          DOTNET_NOLOGO = "1";

          shellHook = ''
            echo "Mi Band Heartrate dev shell (.NET $(dotnet --version))"
          '';
        };

        # ----------------------------------------------------------------
        # Packages
        # ----------------------------------------------------------------
        packages = {
          # Cross-platform GUI frontend (Avalonia).
          miband-heartrate-gui = pkgs.buildDotnetModule {
            pname = "miband-heartrate-gui";
            version = "0.1.0";

            src = ./.;

            projectFile = "MiBandHR.Avalonia/MiBandHR.Avalonia.csproj";
            nugetDeps = ./nuget-deps-gui.json;

            dotnet-sdk = dotnet;
            dotnet-runtime = pkgs.dotnet-runtime_10;

            # The project publishes self-contained with AOT (see the csproj).
            # buildDotnetModule defaults to a framework-dependent build, which
            # would pass `-p:PublishTrimmed=false` and conflict with AOT.
            selfContainedBuild = true;

            # Make sure only cross-platform projects are restored/built.
            dotnetRestoreFlags = [ "-p:EnableWindowsTargeting=false" ];

            runtimeDeps = guiRuntimeDeps;

            executables = [ "MiBandHR.Avalonia" ];

            nativeBuildInputs = [ pkgs.copyDesktopItems ];

            # Give the entry point a distribution-friendly name.
            postFixup = ''
              mv $out/bin/MiBandHR.Avalonia $out/bin/miband-heartrate-gui
            '';

            desktopItems = [
              (pkgs.makeDesktopItem {
                name = "miband-heartrate";
                desktopName = "Mi Band Heartrate";
                comment = "Mi Band heart rate monitor with VRChat OSC output";
                exec = "miband-heartrate-gui";
                icon = "miband-heartrate";
                categories = [ "Utility" ];
              })
            ];

            postInstall = ''
              install -Dm644 ${./icons/icon.png} \
                $out/share/pixmaps/miband-heartrate.png
            '';

            meta = with pkgs.lib; {
              description = "Mi Band heart rate monitor with VRChat OSC output (Avalonia GUI)";
              homepage = "https://github.com/TayouVR/miband-heartrate-osc";
              license = licenses.mit;
              mainProgram = "miband-heartrate-gui";
              platforms = platforms.linux;
            };
          };

          # Cross-platform console frontend.
          miband-heartrate-cli = pkgs.buildDotnetModule {
            pname = "miband-heartrate-cli";
            version = "0.1.0";

            src = ./.;

            projectFile = "MiBandHR.Cli/MiBandHR.Cli.csproj";
            nugetDeps = ./nuget-deps-cli.json;

            dotnet-sdk = dotnet;
            dotnet-runtime = pkgs.dotnet-runtime_10;

            # The project publishes self-contained (see the csproj).
            selfContainedBuild = true;

            dotnetRestoreFlags = [ "-p:EnableWindowsTargeting=false" ];

            runtimeDeps = [ pkgs.icu ];

            executables = [ "MiBandHR.Cli" ];

            # Give the entry point a distribution-friendly name.
            postFixup = ''
              mv $out/bin/MiBandHR.Cli $out/bin/miband-heartrate-cli
            '';

            meta = with pkgs.lib; {
              description = "Mi Band heart rate monitor with VRChat OSC output (console)";
              homepage = "https://github.com/TayouVR/miband-heartrate-osc";
              license = licenses.mit;
              mainProgram = "miband-heartrate-cli";
              platforms = platforms.linux;
            };
          };

          default = self.packages.${system}.miband-heartrate-gui;
        };
      });
}
