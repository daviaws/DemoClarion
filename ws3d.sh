#! /bin/bash
xhost +
docker run --rm -it --name coppelia-sim \
 -e DISPLAY \
 --net=host \
 --privileged \
 brgsil/ws3d-coppelia



