OS_NAME := $(shell uname)
ifeq ($(OS_NAME), Darwin)
OPEN := open
else
OPEN := xdg-open
endif

qa: analyze test

analyze:
	@dotnet format --verify-no-changes

test:
	@dotnet test --verbosity normal

clean:
	@dotnet clean

build: clean
	$(eval VERSION=$(shell git tag --points-at HEAD))
	$(eval VERSION=$(or $(VERSION), 0.0.0))

	@dotnet pack -c Release -p:Company="the native web GmbH" -p:Version=${VERSION} -o ./build/

.PHONY: analyze build clean qa test
