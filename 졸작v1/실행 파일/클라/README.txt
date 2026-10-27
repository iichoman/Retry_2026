=== 유니티 데모 폴더 — 여기에 넣을 것 ===

유니티에서 File -> Build Settings -> Windows -> Build 를 눌러
빌드 결과물을 통째로 이 폴더에 넣는다. 보통 이렇게 생김.

  Retry.exe
  UnityPlayer.dll
  Retry_Data\          <- 폴더. 이게 없으면 실행 안 된다
  MonoBleedingEdge\
  server.ini           <- (이미 있음) 접속할 서버 주소

=== 다른 노트북에 줄 때 ===
  이 폴더를 통째로 USB에 복사한다. exe 하나만 주면 안 된다.
  받은 쪽은 server.ini 의 ip= 만 고치고 Retry.exe 실행.

=== server.ini ===
  [server]
  ip=127.0.0.1     <- 서버 PC의 IPv4 주소(단일 pc로 할거면 로컬 주소인 127.0.0.1 사용)
  port=9000

  서버와 같은 PC에서 돌릴 때는 ip=127.0.0.1
