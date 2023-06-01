Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms
Imports DevExpress.Spreadsheet.Functions
Imports Microsoft.Office.Interop.Excel
Imports System.Globalization
Imports DevExpress.Spreadsheet

Namespace SpreadsheetAddIn

    Public Partial Class Form1
        Inherits DevExpress.XtraBars.Ribbon.RibbonForm

        Private workbook As IWorkbook

        Private excelHelper As ExcelAppHelper

        Private path As String = System.Windows.Forms.Application.StartupPath

        Public Sub New()
            InitializeComponent()
            workbook = spreadsheetControl1.Document
            workbook.LoadDocument("Documents\SphereMaterial.xlsx", DocumentFormat.OpenXml)
            DefineCustomFunctions()
            CalculateCustomFunction()
        End Sub

        Private Sub DefineCustomFunctions()
            ' Open Excel Add-In file in the background.
            excelHelper = New ExcelAppHelper()
            If Not excelHelper.Initialize(path & "\AddIns\SphereMassAddIn.xlam") Then
                MessageBox.Show("Can not start Excel application")
                Return
            End If

            ' Specify a new instance of the custom function and add it to the collection of custom functions in a workbook.
            Dim [function] As AddInFunction = New AddInFunction("SPHEREMASS", excelHelper)
            spreadsheetControl1.Document.CustomFunctions.Add([function])
        End Sub

        Private Sub CalculateCustomFunction()
            Dim worksheet As DevExpress.Spreadsheet.Worksheet = workbook.Worksheets(0)
            worksheet.Range("E4:E8").ArrayFormula = "=SPHEREMASS(D4:D8, C4:C8)"
        End Sub

        Private Sub Form1_FormClosing(ByVal sender As Object, ByVal e As FormClosingEventArgs)
            If excelHelper IsNot Nothing Then excelHelper.Dispose()
        End Sub

        Public Class AddInFunction
            Implements ICustomFunction

            Private Shared parametersField As ParameterInfo() = PrepareParameters()

            Private Shared ErrorToValueDictonary As Dictionary(Of Integer, CellValue) = CreateErrorToValueDictionary()

            Private Shared ValueToErrorDictonary As Dictionary(Of ErrorValueInfo, Integer) = CreateValueToErrorDictionary()

            Private Shared Function CreateErrorToValueDictionary() As Dictionary(Of Integer, CellValue)
                Dim result As Dictionary(Of Integer, CellValue) = New Dictionary(Of Integer, CellValue)()
                result.Add(-2146826281, CellValue.ErrorDivisionByZero)
                result.Add(-2146826246, CellValue.ErrorValueNotAvailable)
                result.Add(-2146826259, CellValue.ErrorName)
                result.Add(-2146826288, CellValue.ErrorNullIntersection)
                result.Add(-2146826252, CellValue.ErrorNumber)
                result.Add(-2146826265, CellValue.ErrorReference)
                result.Add(-2146826273, CellValue.ErrorInvalidValueInFunction)
                Return result
            End Function

            Private Shared Function CreateValueToErrorDictionary() As Dictionary(Of ErrorValueInfo, Integer)
                Dim result As Dictionary(Of ErrorValueInfo, Integer) = New Dictionary(Of ErrorValueInfo, Integer)()
                For Each pair As KeyValuePair(Of Integer, CellValue) In ErrorToValueDictonary
                    result.Add(pair.Value.ErrorValue, pair.Key)
                Next

                Return result
            End Function

            Private ReadOnly excelApp As ExcelAppHelper

            Private ReadOnly nameField As String

            Public Sub New(ByVal name As String, ByVal excelApp As ExcelAppHelper)
                nameField = name
                Me.excelApp = excelApp
            End Sub

            Private Shared Function PrepareParameters() As ParameterInfo()
                ' Missing optional parameters do not result in error message.
                Return New ParameterInfo() {New ParameterInfo(ParameterType.Value, ParameterAttributes.Required), New ParameterInfo(ParameterType.Value, ParameterAttributes.Optional)}
            End Function

'#Region "ICustomFunction Members"
            Public ReadOnly Property Name As String Implements IFunction.Name
                Get
                    Return nameField
                End Get
            End Property

            Public ReadOnly Property Parameters As ParameterInfo() Implements IFunction.Parameters
                Get
                    Return parametersField
                End Get
            End Property

            Public ReadOnly Property ReturnType As ParameterType Implements IFunction.ReturnType
                Get
                    Return ParameterType.Value
                End Get
            End Property

            ' Do not reevaluate cells on every recalculation.
            Public ReadOnly Property Volatile As Boolean Implements IFunction.Volatile
                Get
                    Return False
                End Get
            End Property

            Public Function GetName(ByVal culture As CultureInfo) As String Implements IFunction.GetName
                Return nameField
            End Function

            Public Function Evaluate(ByVal parameters As IList(Of ParameterValue), ByVal context As EvaluationContext) As ParameterValue Implements IFunction.Evaluate
                Dim args As List(Of Object) = New List(Of Object)()
                ' Provide conversion of function parameters for further evaluation in Excel.
                For i As Integer = 0 To parameters.Count - 1
                    args.Add(ConvertParameter(parameters(i)))
                Next

                ' Run Excel macro to evaluate custom function. 
                Dim objResult As dynamic = excelApp.RunMacros(nameField, args)
                ' Convert the function result value to the SpreadsheetControl's value for the correct displaying.
                Return ConvertResultValue(objResult)
            End Function

'#End Region
'#Region "ParameterValue->object conversion"
            ' Convert the SpreadsheetControl's parameters to Excel parameters. 
            Private Function ConvertParameter(ByVal parameter As ParameterValue) As Object
                If parameter.IsNumeric Then
                    Return parameter.NumericValue
                ElseIf parameter.IsBoolean Then
                    Return parameter.BooleanValue
                ElseIf parameter.IsText Then
                    Return parameter.TextValue
                ElseIf parameter.IsError Then
                    Return ValueToErrorDictonary(parameter.ErrorValue)
                ElseIf parameter.IsRange Then
                    Return ConvertRefParameter(parameter.RangeValue)
                ElseIf parameter.IsArray Then
                    Return ConvertArrayParameter(parameter.ArrayValue)
                Else
                    Return Reflection.Missing.Value
                End If
            End Function

            Private Function ConvertArrayParameter(ByVal parameter As CellValue(,)) As Object(,)
                Dim height As Integer = parameter.GetLength(0)
                Dim width As Integer = parameter.GetLength(1)
                Dim result As Object(,) = CType(Array.CreateInstance(GetType(Object), {height, width}, {1, 1}), Object(,))
                For i As Integer = 0 To height - 1
                    For j As Integer = 0 To width - 1
                        Dim value As CellValue = parameter(i, j)
                        If value.IsEmpty Then
                            result(i + 1, j + 1) = Nothing
                        Else
                            result(i + 1, j + 1) = value
                        End If
                    Next
                Next

                Return result
            End Function

            Private Function ConvertRefParameter(ByVal parameter As DevExpress.Spreadsheet.Range) As Object(,)
                Dim height As Integer = parameter.RowCount
                Dim width As Integer = parameter.ColumnCount
                Dim result As Object(,) = CType(Array.CreateInstance(GetType(Object), {height, width}, {1, 1}), Object(,))
                For i As Integer = 0 To height - 1
                    For j As Integer = 0 To width - 1
                        Dim value As CellValue = parameter(i, j).Value
                        result(i + 1, j + 1) = ConvertParameter(value)
                    Next
                Next

                Return result
            End Function

'#End Region
'#Region "object->ParameterValue conversion"
            ' Convert Excel parameters to the SpreadsheetControl's parameters.
            Private Function ConvertResultValue(ByVal value As dynamic) As ParameterValue
                Dim result As ParameterValue
                Dim objArrayRes As Array = TryCast(value, Array)
                If objArrayRes IsNot Nothing Then
                    Dim height As Integer = objArrayRes.GetLength(0)
                    Dim lowerY As Integer = objArrayRes.GetLowerBound(0)
                    Dim width As Integer = objArrayRes.GetLength(1)
                    Dim lowerX As Integer = objArrayRes.GetLowerBound(1)
                    Dim arrayResult As CellValue(,) = New CellValue(height - 1, width - 1) {}
                    For i As Integer = 0 To height - 1
                        For j As Integer = 0 To width - 1
                            arrayResult(i, j) = Me.ConvertResultValueCore(objArrayRes.GetValue(i + lowerY, j + lowerX))
                        Next
                    Next

                    result = arrayResult
                Else
                    result = ConvertResultValueCore(value)
                End If

                Return result
            End Function

            Private Function ConvertResultValueCore(ByVal value As dynamic) As CellValue
                If value Is Nothing Then Return CellValue.Empty
                If TypeOf value Is Integer Then
                    If ErrorToValueDictonary.ContainsKey(value) Then
                        Return ErrorToValueDictonary(value)
                    Else
                        Return value
                    End If
                End If

                Return value
            End Function
'#End Region
        End Class
    End Class
End Namespace
