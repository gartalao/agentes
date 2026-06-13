# Instalación y ejecución

## Requisitos

- **Unity 6000.4.6f1** (Unity 6) instalado vía Unity Hub.
- **Python 3.10+** con `agentpy`, `numpy`, `matplotlib` y `pandas` (o Google
  Colab, que solo requiere instalar `agentpy`).

## 1. Modelo multiagente y análisis (Python)

El corazón del proyecto es el modelo en Python. Genera las métricas, las
gráficas y el plan de control que consume Unity.

- **Google Colab**: subir `notebook/onda_verde_gomez_morin.ipynb` y ejecutar
  todas las celdas (Runtime → Run all). La primera celda instala `agentpy`; el
  notebook es autónomo. Produce el entrenamiento de Q-learning, los diagramas
  espacio-tiempo de los tres escenarios, la comparativa de métricas y
  `plan_control.json`.
- **Local**: desde la raíz del repositorio,

  ```
  pip install agentpy numpy matplotlib pandas
  python -m simulacion.main
  ```

  Esto regenera las gráficas y los CSV en `documentacion/resultados/`, y escribe
  `plan_control.json` tanto ahí como en `unity/Assets/Resources/`.

El paquete `simulacion/` contiene: `corredor.py` (parámetros del corredor),
`microsim.py` (agentes vehiculares IDM y semafóricos, sensado y dinámica),
`control.py` (offsets de onda verde y Q-learning), `metricas.py` (métricas y
ancho de banda), `graficas.py` (figuras) y `exportar.py` (plan JSON para Unity).

## 2. Simulación visual (Unity)

1. En Unity Hub: *Add project from disk* → seleccionar la carpeta `unity/`.
2. Abrir con la versión `6000.4.6f1` (la primera importación tarda unos minutos
   mientras se genera `Library`).
3. Cargar `Assets/Scenes/Corridor.unity` y presionar **Play**.
4. Controles:
   - **Tecla 1**: sin coordinación. **Tecla 2**: onda verde. **Tecla 3**:
     adaptativo (Q-learning). El HUD muestra la estrategia activa.
   - La cámara recorre los tres cruces; se observa el cambio de fase de cada
     semáforo y los movimientos de los agentes.
   - Los semáforos del escenario adaptativo leen `Resources/plan_control.json`
     (la política aprendida en Python) y deciden el verde sensando las colas en
     cada ciclo. Ningún vehículo cruza en rojo.

### Alternativa: Unity Package

En `paquete/` está `corredor_gomez_morin.unitypackage`: crear un proyecto 3D
vacío con la misma versión de Unity, importarlo (*Assets → Import Package →
Custom Package*) y abrir `Assets/Scenes/Corridor.unity`.

## 3. Resultados y documentos

- `documentacion/resultados/`: diagramas espacio-tiempo de los tres escenarios,
  comparativa de métricas, convergencia de Q-learning, colas por cruce,
  sensibilidad a la velocidad, los CSV de métricas y `plan_control.json`.
- `documentacion/capturas/`: capturas de la escena en Unity.
- `documentacion/Reporte_Tecnico_OndaVerde.docx`: reporte técnico final.
- `documentacion/Presentacion_Final_OndaVerde.pptx`: presentación final.
