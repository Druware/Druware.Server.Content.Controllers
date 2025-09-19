#!/bin/sh

# Get the package name from the parent folder

PKGNAME=`pwd`
PKGNAME=`echo $PKGNAME | awk -F "/" '{print $NF}'`

# Update the .nuspec

# lookup the apikey ( if needed )
APIKEY=`defaults read com.trustwin.nuget apikey`
VERSION=`grep -o -p '<version>.*</version>' $PKGNAME.nuspec | sed -n -r "s/^.*<version>(.*)<\/version>.*$/\1/p"` 

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

sed -r -i '' -e "s/^(.*)<version>(.*)<\/version>.*$/\1<version>$VERSION<\/version>/g" $PKGNAME.nuspec 

# build and push

dotnet build . --configuration RELEASE 
nuget pack -OutputDirectory pub -Properties Configuration=Release
# nuget push -Source https://nuget.satori-assoc.com/v3/index.json -ApiKey $APIKEY pub/$PKGNAME.$VERSION.nupkg