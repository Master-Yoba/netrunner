.PHONY: bootstrap restore build test watch-core watch-cards format cli doctor godot-import event-smoke templates package-linux package-macos secrets benchmark-m0 verify m4-gate play m5-client-smoke

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

.PHONY: m5-spec
m5-spec:
	python3 scripts/check-m5-spec.py

.PHONY: ai-benchmark ai-benchmark-smoke ai-benchmark-developer
ai-benchmark:
	bash scripts/ai-benchmark.sh full $(BENCHMARK_ARGS)

ai-benchmark-smoke:
	bash scripts/ai-benchmark.sh smoke $(BENCHMARK_ARGS)

ai-benchmark-developer:
	bash scripts/ai-benchmark.sh developer $(BENCHMARK_ARGS)

.PHONY: ui-capture
ui-capture:
	bash scripts/ui-capture.sh $(CAPTURE_ARGS)

.PHONY: ai-ladder
ai-ladder:
	bash scripts/ai-ladder.sh $(LADDER_ARGS)

.PHONY: fuzz fuzz-developer
fuzz:
	bash scripts/fuzz.sh official $(FUZZ_ARGS)

fuzz-developer:
	bash scripts/fuzz.sh developer $(FUZZ_ARGS)

.PHONY: ai-tactics ai-tactics-verify
ai-tactics:
	bash scripts/ai-tactics.sh $(TACTICAL_ARGS)

ai-tactics-verify:
	bash scripts/ai-tactics.sh --verify-fixtures $(TACTICAL_ARGS)

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

m5-client-smoke:
	bash scripts/m5-client-smoke.sh

.PHONY: play-dev m5-developer-smoke
play-dev:
	bash scripts/play.sh --developer

m5-developer-smoke:
	bash scripts/m5-developer-smoke.sh

.PHONY: m5-gate m5-quick m5-offline-proof
m5-gate:
	bash scripts/m5-gate.sh

m5-quick:
	bash scripts/m5-gate.sh --quick

m5-offline-proof:
	python3 scripts/m5-offline-proof.py --art-cache "$(ART_CACHE)" $(OFFLINE_ARGS)
