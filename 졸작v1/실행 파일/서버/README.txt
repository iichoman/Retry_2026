=== 서버 데모 폴더 — 여기에 넣을 것 ===

[1] Retry_Server.exe        <- 로비 서버
[2] Session_Manager.exe     <- 게임 세션 서버
[3] db.ini                  <- DB 주소
[4] 초기화.sql               <- DB 만드는 스크립트

[1][2] 는 Visual Studio 에서 만든다:
  Retry_Server.sln 열기 -> 상단에서 [Release] [x64] 선택 -> 빌드 -> 솔루션 빌드
  결과: Retry_Server\x64\Release\ 에 exe 두 개가 생긴다. 그 둘만 여기로 복사.
  (.pdb .lib 는 복사하지 않는다)

  ※ Debug 로 빌드한 exe 는 다른 PC에서 안 돌아간다. 반드시 Release.

=== 실행 순서 ===
  1) Retry_Server.exe      (로비, 포트 9000)
  2) Session_Manager.exe   (게임, 포트 9001)
  창 두 개를 다 켜 둔 채로 시연한다. 닫으면 접속이 끊긴다.

=== DB 없이도 돌아간다 ===
  DB에 못 붙으면 로그에 "[DB] 접속 실패" 가 뜨고 그대로 진행된다.
  게임은 정상 동작하고 전적만 저장되지 않는다. 시연은 가능하다.
