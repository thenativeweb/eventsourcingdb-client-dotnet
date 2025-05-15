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

clean:
	@dotnet clean $(LIBRARY)
	@dotnet clean $(TESTS)

build: clean
	$(eval VERSION=$(shell git tag --points-at HEAD))
	$(eval VERSION=$(or $(VERSION), 0.0.0))

	@dotnet pack $(LIBRARY) -c Release -p:Company="the native web GmbH" -p:Version=${VERSION} -o ./build/

.PHONY: analyze build clean qa test
