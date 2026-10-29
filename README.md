# 날씨 위젯 (WeatherWidget)

Windows 11 바탕화면에 Windows 7 가젯처럼 띄워 두는 시계+날씨 위젯. C# WPF (.NET 8).

- 날씨: Open-Meteo (API 키 불필요) — 현재 날씨, 일별 예보(7일/15일), 시간별 5칸(2시간 간격), 30분마다 자동 갱신
- 미세먼지: Open-Meteo Air Quality (PM10/PM2.5, 환경부 등급 기준)
- 지역 검색: Open-Meteo 지오코딩 + OpenStreetMap Nominatim (한글 시/군/구 검색 보완)

## 사용법
- 드래그로 이동 (위치 기억)
- 우클릭 메뉴: 새로고침 / 지역 변경 / 배경 진하기 / 일별 예보 일수(7·15일) / Windows 시작 시 자동 실행 / 종료
- 설정 파일: `%APPDATA%\WeatherWidget\settings.json`
- 자동 실행: `HKCU\...\Run\WeatherWidget` 에 현재 exe 경로를 등록 (exe를 옮기면 다시 켜야 함)

## 빌드
```
dotnet publish -c Release -o publish
```
→ `publish\WeatherWidget.exe` 하나 (실행에 .NET 8 Desktop Runtime 필요)

점검용: `WeatherWidget.exe --snapshot out.png` → 첫 갱신 후 위젯 모습을 PNG로 저장하고 종료.
