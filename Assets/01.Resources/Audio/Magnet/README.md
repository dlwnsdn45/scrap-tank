# 자석 효과음

직접 합성한 게임용 효과음 초안. 44.1 kHz, 16-bit, mono WAV이며 실제 기계 녹음은 아니다.

- magnet_on.wav: 0.50초, 자력 활성화
- magnet_hum_loop.wav: 1.00초, 낮은 전기 진동음. 정수 주기의 파형으로 반복 경계를 구성
- button_detach.wav: 0.22초, 버튼이 뜯기는 금속 팝
- magnet_off.wav: 0.45초, 자력 해제
- button_return.wav: 0.30초, 버튼이 원래 자리에 붙는 금속음

지속음은 별도 AudioSource에서 loop=true로 재생하고 시작과 종료에 0.1초 정도의 음량 페이드를 적용한다. 볼륨을 낮게 유지하고 기존 전체/SFX 음량과 음소거 설정을 반영한다. 다른 소리와 겹치는 상황의 청감 조정은 게임 안에서 필요하다.
