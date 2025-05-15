OS_NAME := $(shell uname)
ifeq ($(OS_NAME), Darwin)
OPEN := open
else
OPEN := xdg-open
endif

LIBRARY := src/EventSourcingDb/EventSourcingDb.csproj
TESTS := src/EventSourcingDb.Tests/EventSourcingDb.Tests.csproj

qa: analyze test

analyze:
	@dotnet format $(LIBRARY) --verify-no-changes
	@dotnet format $(TESTS) --verify-no-changes

test:
	@dotnet test $(TESTS) --verbosity normal

format:
	@dotnet format $(LIBRARY)
	@dotnet format $(TESTS)

clean:
	@dotnet clean $(LIBRARY)
	@dotnet clean $(TESTS)

build: clean
	$(eval VERSION_RAW=$(shell git tag --points-at HEAD))
	$(eval VERSION=$(shell echo $(VERSION_RAW) | sed 's/^v//'))
	$(eval VERSION=$(or $(VERSION), 0.0.0))

	@cp README.md src/EventSourcingDb/README.md
	@dotnet pack $(LIBRARY) -c Release -p:Company="the native web GmbH" -p:Version=${VERSION} -p:PackageReadmeFile=README.md -o ./build/
	@rm src/EventSourcingDb/README.md

.PHONY: analyze \
		build \
		clean \
		format \
		qa \
		test
