using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;

// Plugin para AutoCAD 2024
// Comandos: ALINEARBLOQUES y INSERTARBLOQUES
// Autor: AGRDB - Andrés G. Rodríguez
[assembly: CommandClass(typeof(BlockAlignTool.Commands))]

namespace BlockAlignTool
{
    public class Commands
    {
        // ─────────────────────────────────────────────────────────────────────
        // COMANDO 1: ALINEARBLOQUES
        // Alinea bloques existentes (ya insertados) sobre una polilínea,
        // reubicando su punto de inserción en la polilínea más cercana
        // y rotándolos según la tangente de la misma.
        // ─────────────────────────────────────────────────────────────────────
        [CommandMethod("ALINEARBLOQUES")]
        public void AlignBlocksToPolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor   ed  = doc.Editor;

            ed.WriteMessage("\n=== ALINEAR BLOQUES A POLILÍNEA ===");

            // 1. Seleccionar polilínea
            PromptEntityOptions peoLine = new PromptEntityOptions(
                "\nSelecciona la polilínea de referencia: ");
            peoLine.SetRejectMessage("\nDebe ser una polilínea.");
            peoLine.AddAllowedClass(typeof(Polyline),  true);
            peoLine.AddAllowedClass(typeof(Polyline2d), true);
            peoLine.AddAllowedClass(typeof(Polyline3d), true);

            PromptEntityResult perLine = ed.GetEntity(peoLine);
            if (perLine.Status != PromptStatus.OK) return;

            // 2. Seleccionar bloques
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelecciona los bloques a alinear: ";

            // Filtro: solo referencias de bloque
            SelectionFilter sf = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "INSERT")
            });

            PromptSelectionResult psr = ed.GetSelection(pso, sf);
            if (psr.Status != PromptStatus.OK) return;

            // 3. Opción: mantener rotación original o usar tangente
            PromptKeywordOptions pko = new PromptKeywordOptions(
                "\n¿Rotar bloques según tangente de polilínea? [Sí/No] <Sí>: ");
            pko.Keywords.Add("Sí");
            pko.Keywords.Add("No");
            pko.Keywords.Default = "Sí";
            pko.AllowNone = true;

            PromptResult pkr = ed.GetKeywords(pko);
            bool useRotation = (pkr.Status == PromptStatus.None ||
                                pkr.StringResult == "Sí");

            int count = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Obtener la polilínea
                Curve lineEnt = tr.GetObject(perLine.ObjectId, OpenMode.ForRead) as Curve;
                if (lineEnt == null)
                {
                    ed.WriteMessage("\n✗ El objeto seleccionado no es una curva válida.");
                    tr.Abort();
                    return;
                }

                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    BlockReference br = tr.GetObject(so.ObjectId, OpenMode.ForWrite)
                                         as BlockReference;
                    if (br == null) continue;

                    Point3d currentPos = br.Position;

                    // Encontrar el punto más cercano en la polilínea
                    Point3d closestPt = lineEnt.GetClosestPointTo(currentPos, false);

                    if (useRotation)
                    {
                        // Calcular tangente en ese punto
                        double angle = GetTangentAngle(lineEnt, lineEnt.GetParameterAtPoint(closestPt));
                        br.Rotation = angle;
                    }

                    br.Position = closestPt;
                    count++;
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n✓ {count} bloque(s) alineado(s) correctamente.");
        }


        // ─────────────────────────────────────────────────────────────────────
        // COMANDO 2: INSERTARBLOQUES
        // Inserta un bloque elegido cada X unidades a lo largo de una polilínea.
        // Rota cada bloque según la tangente de la polilínea en ese punto.
        // ─────────────────────────────────────────────────────────────────────
        [CommandMethod("INSERTARBLOQUES")]
        public void InsertBlocksAlongPolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor   ed  = doc.Editor;

            ed.WriteMessage("\n=== INSERTAR BLOQUES A LO LARGO DE POLILÍNEA ===");

            // 1. Seleccionar polilínea
            PromptEntityOptions peoLine = new PromptEntityOptions(
                "\nSelecciona la polilínea: ");
            peoLine.SetRejectMessage("\nDebe ser una polilínea.");
            peoLine.AddAllowedClass(typeof(Polyline),   true);
            peoLine.AddAllowedClass(typeof(Polyline2d), true);
            peoLine.AddAllowedClass(typeof(Polyline3d), true);

            PromptEntityResult perLine = ed.GetEntity(peoLine);
            if (perLine.Status != PromptStatus.OK) return;

            // 2. Nombre del bloque
            PromptStringOptions pso = new PromptStringOptions(
                "\nNombre del bloque a insertar: ");
            pso.AllowSpaces = false;
            PromptResult prBlock = ed.GetString(pso);
            if (prBlock.Status != PromptStatus.OK) return;
            string blockName = prBlock.StringResult.Trim();

            // Verificar que el bloque existe
            using (Transaction checkTr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = checkTr.GetObject(db.BlockTableId, OpenMode.ForRead)
                                       as BlockTable;
                if (!bt.Has(blockName))
                {
                    ed.WriteMessage($"\n✗ El bloque '{blockName}' no existe en el dibujo.");
                    checkTr.Abort();
                    return;
                }
                checkTr.Commit();
            }

            // 3. Distancia de separación
            PromptDistanceOptions pdoSpacing = new PromptDistanceOptions(
                "\nDistancia entre bloques (unidades del dibujo): ");
            pdoSpacing.AllowNegative  = false;
            pdoSpacing.AllowZero      = false;
            pdoSpacing.DefaultValue   = 5.0;

            PromptDoubleResult pdrSpacing = ed.GetDistance(pdoSpacing);
            if (pdrSpacing.Status != PromptStatus.OK) return;
            double spacing = pdrSpacing.Value;

            // 4. Escala
            PromptDoubleOptions pdoScale = new PromptDoubleOptions(
                "\nEscala del bloque <1.0>: ");
            pdoScale.AllowNegative  = false;
            pdoScale.AllowZero      = false;
            pdoScale.DefaultValue   = 1.0;
            pdoScale.AllowNone      = true;

            PromptDoubleResult pdrScale = ed.GetDouble(pdoScale);
            double scale = (pdrScale.Status == PromptStatus.None) ? 1.0 : pdrScale.Value;

            // 5. Desplazamiento lateral (offset perpendicular)
            PromptDoubleOptions pdoOffset = new PromptDoubleOptions(
                "\nDesplazamiento lateral (0 = sobre la polilínea) <0>: ");
            pdoOffset.AllowNegative = true;
            pdoOffset.AllowZero     = true;
            pdoOffset.DefaultValue  = 0.0;
            pdoOffset.AllowNone     = true;

            PromptDoubleResult pdrOffset = ed.GetDouble(pdoOffset);
            double lateralOffset = (pdrOffset.Status == PromptStatus.None)
                                   ? 0.0 : pdrOffset.Value;

            // 6. Insertar en inicio, final, o ambos extremos?
            PromptKeywordOptions pkoCaps = new PromptKeywordOptions(
                "\n¿Insertar en el inicio? [Sí/No] <No>: ");
            pkoCaps.Keywords.Add("Sí");
            pkoCaps.Keywords.Add("No");
            pkoCaps.Keywords.Default = "No";
            pkoCaps.AllowNone = true;

            PromptResult pkrCaps = ed.GetKeywords(pkoCaps);
            bool insertAtStart = (pkrCaps.StringResult == "Sí");

            int insertCount = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead)
                                   as BlockTable;
                BlockTableRecord modelSpace =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite)
                    as BlockTableRecord;

                ObjectId blockDefId = bt[blockName];

                Curve lineEnt = tr.GetObject(perLine.ObjectId, OpenMode.ForRead) as Curve;
                if (lineEnt == null)
                {
                    ed.WriteMessage("\n✗ El objeto seleccionado no es una curva válida.");
                    tr.Abort();
                    return;
                }
                double totalLength = GetPolylineLength(lineEnt);

                if (totalLength <= 0)
                {
                    ed.WriteMessage("\n✗ No se pudo calcular la longitud de la polilínea.");
                    tr.Abort();
                    return;
                }

                ed.WriteMessage($"\nLongitud total de la polilínea: {totalLength:F2} unidades");
                int expectedCount = (int)(totalLength / spacing);
                ed.WriteMessage($"Se insertarán aproximadamente {expectedCount} bloque(s).");

                // Generar puntos a lo largo de la polilínea
                double currentDist = insertAtStart ? 0.0 : spacing;

                while (currentDist <= totalLength + 1e-6)
                {
                    // Punto en la polilínea a la distancia dada
                    // Acotar a la longitud: GetPointAtDist falla si se pasa del final
                    double dist  = Math.Min(currentDist, totalLength);
                    Point3d pt   = lineEnt.GetPointAtDist(dist);
                    double angle = GetTangentAngle(lineEnt, lineEnt.GetParameterAtDistance(dist));

                    // Aplicar offset lateral (perpendicular a la tangente)
                    if (Math.Abs(lateralOffset) > 1e-9)
                    {
                        double perpAngle = angle + Math.PI / 2.0;
                        pt = new Point3d(
                            pt.X + lateralOffset * Math.Cos(perpAngle),
                            pt.Y + lateralOffset * Math.Sin(perpAngle),
                            pt.Z);
                    }

                    // Crear la referencia de bloque
                    BlockReference br = new BlockReference(pt, blockDefId);
                    br.Rotation      = angle;
                    br.ScaleFactors  = new Scale3d(scale);
                    br.Layer         = lineEnt.Layer; // Hereda la capa de la polilínea

                    modelSpace.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);

                    // Agregar atributos si el bloque los tiene
                    BlockTableRecord blockDef =
                        tr.GetObject(blockDefId, OpenMode.ForRead) as BlockTableRecord;
                    if (blockDef.HasAttributeDefinitions)
                    {
                        foreach (ObjectId attId in blockDef)
                        {
                            AttributeDefinition attDef =
                                tr.GetObject(attId, OpenMode.ForRead) as AttributeDefinition;
                            if (attDef == null || attDef.Constant) continue;

                            AttributeReference attRef = new AttributeReference();
                            attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                            br.AttributeCollection.AppendAttribute(attRef);
                            tr.AddNewlyCreatedDBObject(attRef, true);
                        }
                    }

                    insertCount++;
                    currentDist += spacing;
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n✓ {insertCount} bloque(s) '{blockName}' insertado(s) correctamente.");
        }


        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // Polyline, Polyline2d y Polyline3d derivan de Curve, así que se
        // usa la API de Curve para las tres en lugar de un caso por tipo.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Obtiene la longitud total de la polilínea.</summary>
        private double GetPolylineLength(Curve curve)
        {
            try
            {
                return curve.GetDistanceAtParameter(curve.EndParam);
            }
            catch (System.Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Devuelve el ángulo de la tangente (en radianes, sobre el plano XY)
        /// en el parámetro dado de la curva.
        /// </summary>
        private double GetTangentAngle(Curve curve, double param)
        {
            // En el parámetro final no hay tramo siguiente: se retrocede un poco.
            double safeParam = Math.Max(curve.StartParam,
                                        Math.Min(param, curve.EndParam - 1e-6));
            try
            {
                Vector3d d = curve.GetFirstDerivative(safeParam);
                if (Math.Abs(d.X) > 1e-12 || Math.Abs(d.Y) > 1e-12)
                    return Math.Atan2(d.Y, d.X);
            }
            catch (System.Exception)
            {
                // Se intenta con diferencia finita más abajo.
            }

            try
            {
                Point3d p1 = curve.GetPointAtParameter(safeParam);
                Point3d p2 = curve.GetPointAtParameter(
                    Math.Min(safeParam + 0.001, curve.EndParam));
                return Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
            }
            catch (System.Exception)
            {
                return 0.0;
            }
        }
    }
}
