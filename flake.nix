# Nix/NixOS environment pulling in all required dependencies required to work with the current version of Farseer.
{
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";

  outputs = {
    self,
    nixpkgs,
  }: let
    supportedSystems = ["x86_64-linux" "x86_64-darwin" "aarch64-linux" "aarch64-darwin"];
    forAllSystems = nixpkgs.lib.genAttrs supportedSystems;
    pkgsPerSystem = forAllSystems (system:
      import nixpkgs {
        system = "${system}";
        config = {
          # Vintage Story is unfree
          allowUnfree = true;
        };
      });
  in rec {
    packages = forAllSystems (system:
      import ./packages.nix {
        pkgs = pkgsPerSystem.${system};
      });
    devShells = forAllSystems (system: let
      pkgs = pkgsPerSystem.${system};
      lib = pkgs.lib;
      # for VS >= 1.22
      dotnetPkg = with pkgs.dotnetCorePackages;
        combinePackages [
          sdk_10_0
        ];
    in {
      default = pkgs.mkShell {
        packages = with pkgs; [
          dotnetPkg
          # Used for running the ZZCakeBuild binary
          steam-run
        ];
        LD_LIBRARY_PATH = lib.makeLibraryPath [pkgs.stdenv.cc.cc.lib];
        VINTAGE_STORY = "${packages.${system}.vintagestory-1-22-0}/share/vintagestory";
        DOTNET_ROOT = "${dotnetPkg}";
        FrameworkVersion = "net10.0";
      };
    });
  };
}
