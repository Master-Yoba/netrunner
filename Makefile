.PHONY: bootstrap restore build test format cli godot-import verify

bootstrap:
	./scripts/bootstrap.sh

restore:
	bash -c 'source scripts/env.sh && dotnet restore Netrunner.sln'

build:
	bash -c 'source scripts/env.sh && dotnet build Netrunner.sln --no-restore'

test:
	bash -c 'source scripts/env.sh && dotnet test Netrunner.sln --no-build'

format:
	bash -c 'source scripts/env.sh && dotnet format Netrunner.sln'

cli:
	bash -c 'source scripts/env.sh && dotnet run --project src/Netrunner.Cli -- self-test --seed 42'

godot-import:
	bash -c 'source scripts/env.sh && "$${GODOT4}" --headless --editor --path src/Netrunner.Godot --quit'

verify:
	./scripts/verify.sh

