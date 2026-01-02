#!/bin/sh

# Get the package name from the parent folder

PKGNAME=`pwd`
PKGNAME=`echo $PKGNAME | awk -F "/" '{print $NF}'`

VERSION=`grep -o -p '<Version>.*</Version>' $PKGNAME.csproj | sed -n -r "s/^.*<Version>(.*)<\/Version>.*$/\1/p"` 

# parse the number
MAJOR=`echo $VERSION | awk '{split($0,a,"."); print a[1]}'`
MINOR=`echo $VERSION | awk '{split($0,a,"."); print a[2]}'`
REVISION=`echo $VERSION | awk '{split($0,a,"."); print a[3]}'`

# increment the revision
# if $1 == '--major' 
if [ "$1" = '--major' ]; then 
  MAJOR=$((MAJOR+1))
  MINOR=0
  REVISION=0  
# if $1 == '--minor' 
elif [ "$1" = '--minor' ]; then
  MINOR=$((MINOR+1))
  REVISION=0
# else increment the revision
else
  REVISION=$((REVISION+1))
fi 
VERSION=$MAJOR.$MINOR.$REVISION

sed -r -i '' -e "s/^(.*)<Version>(.*)<\/Version>.*$/\1<Version>$VERSION<\/Version>/g" $PKGNAME.csproj 

# build and push

dotnet build . --configuration Release 
nuget pack -OutputDirectory pub -Properties Configuration=Release

## Nuget Push
APIKEY=`defaults read org.nuget.api apikey`
dotnet nuget push --source https://api.nuget.org/v3/index.json --api-key $APIKEY pub/$PKGNAME.$VERSION.nupkg

## Satori Push
APIKEY=`defaults read com.trustwin.nuget apikey`
dotnet nuget push --source https://nuget.satori-assoc.com/v3/index.json --api-key $APIKEY pub/$PKGNAME.$VERSION.nupkg

