#! /bin/bash
ID=`id -u`
GID=`id -g`
docker run \
-v .:/home/$USER \
-v /tmp:/tmp \
--rm \
--name monodevelop \
-h monodevelop \
-w /home/$USER \
--net=host \
-it \
--entrypoint /bin/bash \
zachdeibert/monodevelop \
-c "echo Starting monodevelop from docker;\
if ! getent group $GID > /dev/null; then groupadd -g $GID $USER;fi;\
useradd -r -d /home/$USER -s /bin/bash -g $(id -gn) -G sudo -u $ID $USER;
su -c '/launch.sh $DISPLAY' $USER"
