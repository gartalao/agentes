# Coordinación Multiagente de Intersecciones Semafóricas
### Corredor Av. Gómez Morín, San Pedro Garza García — TC2008B

Simulación multiagente de tres cruces reales de la Av. Gómez Morín (Av. Alfonso
Reyes, Magnolia y Av. del Roble) con agentes vehiculares y semafóricos. El
proyecto compara tres estrategias de control sobre el mismo corredor y las
mismas métricas:

1. **Sin coordinación** — tiempo fijo, cada semáforo aislado (baseline).
2. **Onda verde** — tiempo fijo coordinado por offsets calculados con la
   velocidad de sincronía (heurística de progresión).
3. **Adaptativo (Q-learning)** — cada semáforo sensa sus colas (detector tipo
   cámara/sensor) y decide el reparto de verde en lazo cerrado con una política
   aprendida por refuerzo. No usa tiempos preprogramados: es desplegable.

El modelo multiagente, la coordinación y el análisis se hacen en Python
(AgentPy); Python exporta a JSON los planes y la política aprendida, y Unity los
ejecuta sobre la maqueta 3D del corredor.

## Equipo 1 — Gpo. 301

| Integrante | Matrícula |
|---|---|
| Karla Alessandra Sánchez Saviñón | A01177120 |
| Emiliano Carrizales Becerra | A00824311 |
| Daniel Muñoz Lozano | A01721797 |
| Adán Ehécatl González Flores | A00841625 |
| Patricio Javier Garza Ríos | A00841942 |

## Estructura del repositorio

```
agentes/
├── simulacion/       Modelo multiagente en Python (AgentPy): agentes, control,
│                     Q-learning, métricas y exportación del plan a JSON
├── notebook/         Notebook de onda verde (Google Colab / Jupyter)
├── unity/            Proyecto de Unity con la escena de los 3 cruces
│                     (Assets/Scenes/Corridor.unity)
├── paquete/          Unity Package del corredor
└── documentacion/    Instalación, resultados, capturas, reporte y presentación
```

## Inicio rápido

1. **Modelo y análisis (Python)**: subir `notebook/onda_verde_gomez_morin.ipynb`
   a Google Colab y ejecutar todas las celdas (instala `agentpy` en la primera).
   Alternativamente, desde la raíz del repo: `python -m simulacion.main`, que
   regenera las gráficas, las métricas y `plan_control.json`.
2. **Ver la simulación (Unity)**: abrir `unity/` con Unity `6000.4.6f1`, cargar
   `Assets/Scenes/Corridor.unity` y presionar Play. Con las teclas **1 / 2 / 3**
   se conmuta entre sin coordinación, onda verde y adaptativo.
3. Guía completa en [`documentacion/instalacion.md`](documentacion/instalacion.md).

## Qué hay en la simulación

- **3 intersecciones reales** modeladas en Blender con base en la geometría del
  corredor: dos pasos a desnivel (Alfonso Reyes y del Roble) y un cruce a nivel
  (Magnolia).
- **Agentes vehiculares** con seguimiento vehicular IDM; por construcción nunca
  cruzan en rojo.
- **Agentes semafóricos** que sensan las colas de sus accesos y, según la
  estrategia, fijan o aprenden el reparto de verde; coordinación por reloj común
  más offset.
- **Q-learning** entrenado en Python (estado = colas discretizadas, acción =
  verde del corredor, recompensa = − colas) cuya política ejecuta Unity en vivo.

## Resultados principales (demanda nominal)

| Estrategia | Demora | Velocidad | Paradas/veh | Coordinación |
|---|---|---|---|---|
| Sin coordinación | 96.8 s | 24.7 km/h | 2.56 | 26 % |
| Onda verde | 39.1 s | 35.3 km/h | 0.99 | 67 % |
| Adaptativo (Q-learning) | 29.9 s | 38.0 km/h | 0.67 | 78 % |

Coordinar reduce la demora 60 % frente a no coordinar; el control adaptativo la
reduce otro 24 % y, en hora pico, eleva el throughput 19 %. El ancho de banda de
la onda verde pasa de 8.2 s (sin coordinar) a 45.9 s (100 % del verde). Detalles
en `documentacion/resultados/` y en el reporte técnico.
