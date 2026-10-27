@echo off
rem Upload the HatchMerge server to a VPS and run the installer.
rem Needs Windows 10/11 (ssh and scp are built in).
cd /d "%~dp0.."
set /p VPS=VPS IP address: 
set DOMAIN=
set /p DOMAIN=Domain (press Enter if you have none): 
echo.
echo You will be asked for the VPS root password up to 3 times.
ssh root@%VPS% "mkdir -p /root/hatchmerge-server/deploy"
scp hatchmerge_server.py root@%VPS%:/root/hatchmerge-server/
scp deploy\install.sh deploy\backup.sh root@%VPS%:/root/hatchmerge-server/deploy/
ssh -t root@%VPS% "sed -i 's/\r$//' /root/hatchmerge-server/deploy/*.sh && bash /root/hatchmerge-server/deploy/install.sh %DOMAIN%"
echo.
pause
