.PHONY: bootstrap restore build test watch-core watch-cards format cli doctor godot-import event-smoke templates package-linux package-macos secrets benchmark-m0 verify m4-gate play

bootstrap:
	./scripts/bootstrap.sh

restore:
	bash -c 'source scripts/env.sh && dotnet restore Netrunner.sln'

build:
	bash -c 'source scripts/env.sh && dotnet build Netrunner.sln --no-restore'

test:
	bash -c 'source scripts/env.sh && dotnet test Netrunner.sln --no-build'

watch-core:
	bash -c 'source scripts/env.sh && dotnet watch --project tests/Netrunner.Core.Tests test --no-restore'

watch-cards:
	bash -c 'source scripts/env.sh && dotnet watch --project tests/Netrunner.Cards.Tests test --no-restore'

format:
	bash -c 'source scripts/env.sh && dotnet format Netrunner.sln'

cli:
	bash -c 'source scripts/env.sh && dotnet run --project src/Netrunner.Cli -- self-test --seed 42'

doctor:
	bash -c 'source scripts/env.sh && dotnet run --project src/Netrunner.Cli -- doctor --json'

godot-import:
	bash -c 'source scripts/env.sh && "$${GODOT4}" --headless --editor --path src/Netrunner.Godot --quit-after 60'

event-smoke:
	bash -c 'source scripts/env.sh && XDG_DATA_HOME="$${NETRUNNER_GODOT_DATA}" "$${GODOT4}" --headless --path src/Netrunner.Godot -- --smoke-test'

templates:
	./scripts/install-export-templates.sh

package-linux:
	./scripts/package-linux.sh

package-macos:
	./scripts/package-macos.sh

secrets:
	./scripts/check-secrets.sh

benchmark-m0:
	./scripts/benchmark-m0.sh

verify:
	./scripts/verify.sh

m4-gate:
	./scripts/verify.sh --m4

play:
	./scripts/play.sh $(PLAY_ARGS)

.PHONY: interaction-lab
interaction-lab:
	./scripts/interaction-lab.sh

.PHONY: card-art-fetch card-art-validate card-art-pack card-art-unpack
card-art-fetch card-art-validate card-art-pack card-art-unpack:
	@./scripts/card-art.sh $(patsubst card-art-%,%,$@) $(ART_ARGS)
