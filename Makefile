OS_NAME := $(shell uname)
ifeq ($(OS_NAME), Darwin)
OPEN := open
else
OPEN := xdg-open
endif

LIBRARY := src/EventSourcingDb/EventSourcingDb.csproj
TESTS := src/EventSourcingDb.Tests/EventSourcingDb.Tests.csproj

qa: restore analyze test

restore:
	@dotnet restore $(LIBRARY)
	@dotnet restore $(TESTS)

analyze:
	@dotnet format $(LIBRARY) --no-restore --verify-no-changes
	@dotnet format $(TESTS) --no-restore --verify-no-changes

test:
	@dotnet test $(TESTS) --verbosity normal

format: restore
	@dotnet format $(LIBRARY) --no-restore
	@dotnet format $(TESTS) --no-restore

clean:
	@dotnet clean $(LIBRARY)
	@dotnet clean $(TESTS)

build: clean
	$(eval VERSION_RAW=$(shell git tag --points-at HEAD))
	$(eval VERSION=$(shell echo $(VERSION_RAW) | sed 's/^v//'))
	$(eval VERSION=$(or $(VERSION), 0.0.0))

	@dotnet pack $(LIBRARY) -c Release -p:Company="the native web GmbH" -p:Version=${VERSION} -o ./build/

.PHONY: analyze \
		build \
		clean \
		format \
		qa \
		restore \
		test
